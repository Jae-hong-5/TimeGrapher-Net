using System.Globalization;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Beat Noise (Scope 1 + Scope 2), rendered from the cumulative
/// BeatSegmentsSnapshot the frame carries.
///
/// Scope 1: the selected-or-latest beat segment on the main plot (X in ms,
/// clipped to the 20/200/400 ms range), with pooled A / C-peak / C-onset
/// marker lines. By default the main trace draws the real un-rectified RAW
/// waveform as its per-point min/max outlines (the actual bipolar signal,
/// symmetric about zero — not a mirror of the envelope); the Envelope toggle
/// shows the rectified envelope for readability and raw-unavailable segments
/// fall back to the negated envelope. The aligned strip lane below compresses
/// the 8 most recent beats side by side; pressing a strip selects it (a pooled
/// span highlights the slot).
///
/// Scope 2: the two phase-alternating averaged lanes on a fixed 0-20 ms axis,
/// vertically offset and labeled Tic/Toc, with the per-lane
/// average signal level and Σ progress in the readout below.
///
/// All plottables refill in place; re-renders only when the snapshot version
/// changes, so coalesced or repeated frames cost nothing. Segments reference
/// pooled Core buffers that stay valid only until rotated out, so each new
/// snapshot version is deep-copied into a UI-owned cache (CopyForCache ->
/// _lastSnapshot); later interaction handlers and paused-review re-renders read
/// that cached copy, never a recycled Core pool buffer.
/// </summary>
internal sealed class BeatNoiseScopeRenderer
{
    public const int DefaultRangeMs = 400;
    public const int InitialRangeMs = 20;

    private const int StripPointBudget = 200;
    internal const float StripLeftAxisSizePx = 68.0f;
    private const float MarkerLegendLineWidth = 2.0f;
    private const byte StripDividerAlpha = 140;
    private const double StripSlotLabelY = 0.97;
    private const double Lane2Baseline = 0.0;
    private const double Lane1Baseline = 1.2;
    private const byte SelectionFillAlpha = 48;

    private readonly AvaPlot _mainPlot;
    private readonly AvaPlot _stripPlot;
    private readonly AvaPlot _averagePlot;
    private readonly TextBlock _averageText;

    private readonly List<double> _mainX = new();
    private readonly List<double> _mainY = new();
    private readonly List<double> _mainYMirror = new();
    private readonly List<double>[] _stripX;
    private readonly List<double>[] _stripY;
    private readonly List<double> _lane1X = new();
    private readonly List<double> _lane1Y = new();
    private readonly List<double> _lane2X = new();
    private readonly List<double> _lane2Y = new();
    private readonly List<double> _mainALegendX = new();
    private readonly List<double> _mainALegendY = new();
    private readonly List<double> _mainCLegendX = new();
    private readonly List<double> _mainCLegendY = new();
    private readonly List<StableStripWindow> _stable400MsStripWindows = new();

    private sealed class StableStripWindow
    {
        public StableStripWindow(long bucket, BeatSegment segment, IReadOnlyList<BeatSegment> markerSegments)
        {
            Bucket = bucket;
            Segment = segment;
            MergeMarkerSegments(markerSegments);
        }

        public long Bucket { get; }

        public BeatSegment Segment { get; }

        public List<BeatSegment> MarkerSegments { get; } = new();

        public void MergeMarkerSegments(IReadOnlyList<BeatSegment> markerSegments)
        {
            foreach (BeatSegment markerSegment in markerSegments)
            {
                if (markerSegment.StartTimeS < Segment.StartTimeS
                    || markerSegment.StartTimeS >= Segment.StartTimeS + DefaultRangeMs / 1000.0)
                {
                    continue;
                }

                if (MarkerSegments.Any(existing =>
                        Math.Abs(existing.StartTimeS - markerSegment.StartTimeS) < 1e-9
                        && Math.Abs(existing.AOffsetMs - markerSegment.AOffsetMs) < 1e-6))
                {
                    continue;
                }

                MarkerSegments.Add(markerSegment);
            }

            MarkerSegments.Sort((left, right) => left.StartTimeS.CompareTo(right.StartTimeS));
        }
    }

    private Scatter? _mainScatter;
    private Scatter? _mirrorScatter;
    private Scatter? _mainALegendScatter;
    private Scatter? _mainCLegendScatter;
    private readonly List<VerticalLine> _dynamicAMarkers = new();
    private readonly List<VerticalLine> _dynamicCPeakMarkers = new();
    private VerticalLine? _cOnsetMarker;
    private Text? _weakSignalLabel;
    private readonly SignalQualityOverlayState _signalQualityOverlay = new();
    private ReviewCursorLayer? _reviewCursor;
    private readonly Scatter?[] _stripScatters;
    private readonly VerticalLine?[] _stripDividers;
    private readonly Text?[] _stripSlotLabels;
    private readonly List<VerticalLine> _stripAMarkers = new();
    private readonly List<VerticalLine> _stripCMarkers = new();
    private HorizontalSpan? _selectionSpan;
    private Scatter? _lane1Scatter;
    private Scatter? _lane2Scatter;
    private Text? _lane1SignalLabel;
    private Text? _lane2SignalLabel;
    private readonly List<double>[] _lane1MilestoneX = new List<double>[5];
    private readonly List<double>[] _lane1MilestoneY = new List<double>[5];
    private readonly List<double>[] _lane2MilestoneX = new List<double>[5];
    private readonly List<double>[] _lane2MilestoneY = new List<double>[5];
    private readonly Scatter?[] _lane1MilestoneScatters = new Scatter?[5];
    private readonly Scatter?[] _lane2MilestoneScatters = new Scatter?[5];

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private int _rangeMs = InitialRangeMs;
    private bool _showAbsoluteValue;
    private bool _useCOnset;
    private int? _selectedSlot;
    private BeatSegmentsSnapshot? _lastSnapshot;
    private BeatNoiseScopeViewMode _viewMode = BeatNoiseScopeViewMode.EnvelopeAndStrip;
    private double? _lastCursorTimeS;
    private double? _mainYLower;
    private double? _mainYUpper;

    public BeatNoiseScopeRenderer(
        AvaPlot mainPlot,
        AvaPlot stripPlot,
        AvaPlot averagePlot,
        TextBlock averageText,
        bool useCOnset = false)
    {
        _mainPlot = mainPlot;
        _stripPlot = stripPlot;
        _averagePlot = averagePlot;
        _averageText = averageText;
        _useCOnset = useCOnset;

        _stripX = new List<double>[BeatNoiseScopeLogic.StripCount];
        _stripY = new List<double>[BeatNoiseScopeLogic.StripCount];
        _stripScatters = new Scatter?[BeatNoiseScopeLogic.StripCount];
        _stripDividers = new VerticalLine?[BeatNoiseScopeLogic.StripCount - 1];
        _stripSlotLabels = new Text?[BeatNoiseScopeLogic.StripCount];
        for (int i = 0; i < BeatNoiseScopeLogic.StripCount; i++)
        {
            _stripX[i] = new List<double>();
            _stripY[i] = new List<double>();
        }

        for (int i = 0; i < 5; i++)
        {
            _lane1MilestoneX[i] = new List<double>();
            _lane1MilestoneY[i] = new List<double>();
            _lane2MilestoneX[i] = new List<double>();
            _lane2MilestoneY[i] = new List<double>();
        }

        _mainPlot.UserInputProcessor.Disable();
        _stripPlot.UserInputProcessor.Disable();
        _averagePlot.UserInputProcessor.Disable();
    }

    public int RangeMs => _rangeMs;

    public BeatNoiseScopeViewMode ViewMode => _viewMode;

    public void SetUseCOnset(bool enabled)
    {
        if (_useCOnset == enabled)
        {
            return;
        }

        _useCOnset = enabled;
        if (_lastSnapshot is { } snapshot)
        {
            RenderMain(snapshot);
            RenderStrips(snapshot);
            _mainPlot.Refresh();
            _stripPlot.Refresh();
        }
    }

    private void ClearMilestoneLines()
    {
        for (int i = 0; i < 5; i++)
        {
            _lane1MilestoneX[i].Clear();
            _lane1MilestoneY[i].Clear();
            _lane2MilestoneX[i].Clear();
            _lane2MilestoneY[i].Clear();
            if (_lane1MilestoneScatters[i] != null)
            {
                _lane1MilestoneScatters[i]!.IsVisible = false;
            }

            if (_lane2MilestoneScatters[i] != null)
            {
                _lane2MilestoneScatters[i]!.IsVisible = false;
            }
        }
    }

    public void SetViewMode(BeatNoiseScopeViewMode mode)
    {
        if (_viewMode == mode)
        {
            return;
        }

        _viewMode = mode;
        _selectedSlot = null;
        ApplyRangeLimits();
        if (_lastSnapshot is { } snapshot)
        {
            RenderMain(snapshot);
            RenderStrips(snapshot);
            RenderAverageView(snapshot);
        }

        RefreshAll();
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_mainPlot.Plot);
        ApplyPlotTheme(_stripPlot.Plot);
        ApplyPlotTheme(_averagePlot.Plot);
        ApplySeriesTheme();
        RefreshAll();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _lastSnapshot = null;
        _selectedSlot = null;
        _lastCursorTimeS = null;
        _mainYLower = null;
        _mainYUpper = null;
        _stable400MsStripWindows.Clear();
        ClearMilestoneLines();
        _averageText.Text = BeatNoiseScopeLogic.AverageLine(BeatNoiseAverageSnapshot.Empty);

        Plot main = _mainPlot.Plot;
        main.Clear();
        _mainX.Clear();
        _mainY.Clear();
        _mainYMirror.Clear();
        _mainALegendX.Clear();
        _mainALegendY.Clear();
        _mainCLegendX.Clear();
        _mainCLegendY.Clear();
        ApplyPlotTheme(main);
        main.Axes.Left.TickLabelStyle.IsVisible = false;
        main.YLabel("Signal Level");
        main.XLabel("ms");
        _mainScatter = main.Add.Scatter(_mainX, _mainY);
        _mainScatter.LineWidth = 1;
        _mainScatter.MarkerStyle.IsVisible = false;
        _mirrorScatter = main.Add.Scatter(_mainX, _mainYMirror);
        _mirrorScatter.LineWidth = 1;
        _mirrorScatter.MarkerStyle.IsVisible = false;
        _mirrorScatter.IsVisible = !_showAbsoluteValue;
        _mainALegendScatter = main.Add.Scatter(_mainALegendX, _mainALegendY);
        _mainALegendScatter.LineWidth = MarkerLegendLineWidth;
        _mainALegendScatter.LinePattern = GraphLinePatterns.VerticalGuide;
        _mainALegendScatter.MarkerStyle.IsVisible = false;
        _mainALegendScatter.LegendText = "A";
        _mainCLegendScatter = main.Add.Scatter(_mainCLegendX, _mainCLegendY);
        _mainCLegendScatter.LineWidth = MarkerLegendLineWidth;
        _mainCLegendScatter.LinePattern = GraphLinePatterns.VerticalGuide;
        _mainCLegendScatter.MarkerStyle.IsVisible = false;
        _mainCLegendScatter.LegendText = "C";
        main.ShowLegend();
        _dynamicAMarkers.Clear();
        _dynamicCPeakMarkers.Clear();
        _cOnsetMarker = AddMarker(main, GraphLinePatterns.VerticalGuide);
        _weakSignalLabel = AddWeakSignalLabel(main);
        _signalQualityOverlay.Reset();
        _reviewCursor = AddCursor(main);
        ApplyRangeLimits();
        // Cap zoom-out/pan to the initial full output on both axes: X to the
        // selected range [0, _rangeMs] and Y to the live auto-fit
        // [_mainYLower, _mainYUpper]. Zoom-in still works; zoom-out cannot pass
        // the first-output extent.
        main.Axes.Rules.Clear();
        main.Axes.Rules.Add(new ScopeViewBoundsRule(
            main.Axes.Bottom, main.Axes.Left,
            () => (0.0, _rangeMs),
            () => _mainYLower is double lo && _mainYUpper is double hi && hi > lo
                ? ((double, double)?)(lo, hi)
                : null));
        main.Axes.Left.MinimumSize = StripLeftAxisSizePx;
        main.Axes.Left.MaximumSize = StripLeftAxisSizePx;

        // Keep the strip visually minimal while reserving the same left axis
        // width as the plot above it, so the data areas line up.
        Plot strip = _stripPlot.Plot;
        strip.Clear();
        _stripAMarkers.Clear();
        _stripCMarkers.Clear();
        ApplyPlotTheme(strip);
        strip.XLabel("Oldest -> Newest");
        strip.Grid.IsVisible = false;
        strip.Axes.Bottom.TickLabelStyle.IsVisible = false;
        strip.Axes.Left.TickLabelStyle.IsVisible = false;
        strip.Axes.Right.TickLabelStyle.IsVisible = false;
        strip.Axes.Top.TickLabelStyle.IsVisible = false;
        strip.Axes.Left.MinimumSize = StripLeftAxisSizePx;
        strip.Axes.Left.MaximumSize = StripLeftAxisSizePx;
        strip.Axes.SetLimits(0, BeatNoiseScopeLogic.StripCount, 0, 1);
        for (int i = 0; i < _stripDividers.Length; i++)
        {
            _stripDividers[i] = strip.Add.VerticalLine(i + 1.0);
            _stripDividers[i]!.LineWidth = 2;
            _stripDividers[i]!.EnableAutoscale = false;
        }

        _selectionSpan = strip.Add.HorizontalSpan(0.0, 1.0);
        _selectionSpan.IsVisible = false;
        for (int i = 0; i < BeatNoiseScopeLogic.StripCount; i++)
        {
            _stripX[i].Clear();
            _stripY[i].Clear();
            _stripScatters[i] = strip.Add.Scatter(_stripX[i], _stripY[i]);
            _stripScatters[i]!.LineWidth = 1;
            _stripScatters[i]!.MarkerStyle.IsVisible = false;
        }
        for (int i = 0; i < BeatNoiseScopeLogic.StripCount; i++)
        {
            _stripSlotLabels[i] = AddStripSlotLabel(strip);
        }

        Plot average = _averagePlot.Plot;
        average.Clear();
        for (int i = 0; i < 5; i++)
        {
            _lane1MilestoneScatters[i] = null;
            _lane2MilestoneScatters[i] = null;
        }
        _lane1X.Clear();
        _lane1Y.Clear();
        _lane2X.Clear();
        _lane2Y.Clear();
        ApplyPlotTheme(average);
        average.XLabel("ms");
        average.YLabel("Signal Level");
        average.Axes.Left.TickLabelStyle.IsVisible = false;

        _lane1Scatter = average.Add.Scatter(_lane1X, _lane1Y);
        _lane1Scatter.LineWidth = 1;
        _lane1Scatter.MarkerStyle.IsVisible = false;
        _lane1Scatter.LegendText = "Tic";
        _lane2Scatter = average.Add.Scatter(_lane2X, _lane2Y);
        _lane2Scatter.LineWidth = 1;
        _lane2Scatter.MarkerStyle.IsVisible = false;
        _lane2Scatter.LegendText = "Toc";
        _lane1SignalLabel = AddAverageSignalLabel(average);
        _lane2SignalLabel = AddAverageSignalLabel(average);
        average.ShowLegend();
        average.Axes.SetLimits(0, BeatNoiseAverager.LaneWindowMs, -0.1, Lane1Baseline + 1.15);
        // Same zoom-out cap as the main plot, bounded to the average view's
        // fixed extent so zoom-out never passes the initial output.
        average.Axes.Rules.Clear();
        average.Axes.Rules.Add(new ScopeViewBoundsRule(
            average.Axes.Bottom, average.Axes.Left,
            () => (0.0, BeatNoiseAverager.LaneWindowMs),
            () => (-0.1, Lane1Baseline + 1.15)));
        average.Axes.Left.MinimumSize = StripLeftAxisSizePx;
        average.Axes.Left.MaximumSize = StripLeftAxisSizePx;

        ApplySeriesTheme();
        RefreshAll();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    /// <summary>Scope 1 range selector (20 / 200 / 400 ms).</summary>
    public void SetRangeMs(int rangeMs)
    {
        if (_rangeMs == rangeMs)
        {
            return;
        }

        _rangeMs = rangeMs;
        ApplyRangeLimits();
        if (_lastSnapshot is { } snapshot)
        {
            RenderMain(snapshot);
            RenderStrips(snapshot);
        }

        _mainPlot.Refresh();
        _stripPlot.Refresh();
    }

    public void SetAbsoluteValue(bool enabled)
    {
        if (_showAbsoluteValue == enabled)
        {
            return;
        }

        _showAbsoluteValue = enabled;
        _mainYLower = null;
        _mainYUpper = null;
        if (_mirrorScatter != null)
        {
            _mirrorScatter.IsVisible = !enabled;
        }

        if (_lastSnapshot is { } snapshot)
        {
            RenderMain(snapshot);
        }

        _mainPlot.Refresh();
    }

    public void SelectStripAtPixel(double x, double width)
    {
        double fraction = BeatNoiseScopeLogic.StripFractionFromPixel(x, width, StripLeftAxisSizePx);
        SelectStripAtFraction(fraction);
    }

    /// <summary>Strip-lane press at the given horizontal fraction (0..1) of the data area.</summary>
    public void SelectStripAtFraction(double fraction)
    {
        if (_lastSnapshot is not { } snapshot)
        {
            return;
        }

        int slot = BeatNoiseScopeLogic.StripSlotFromFraction(fraction);
        if (slot < 0)
        {
            // Click outside the strip data area (e.g. on the reserved left axis):
            // keep the current selection rather than jumping to the oldest slot.
            return;
        }

        _selectedSlot = _viewMode == BeatNoiseScopeViewMode.AverageAndStrip
            ? NextAveragePairSelection(_selectedSlot, snapshot, slot)
            : NextBeatScopeSelection(_selectedSlot, snapshot, slot, _rangeMs);
        RenderMain(snapshot);
        RenderAverageView(snapshot);
        UpdateSelectionSpan(snapshot);
        // The review cursor's offset is relative to the DISPLAYED segment's
        // window start; changing the selection while paused must recompute it
        // (no new frame arrives to do so), or the cursor stays drawn at the
        // previous segment's offset — possibly outside the new beat entirely.
        UpdateReviewCursor(_lastCursorTimeS);
        _mainPlot.Refresh();
        _stripPlot.Refresh();
        _averagePlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        BeatSegmentsSnapshot? snapshot = frame.BeatSegments;
        // Cache a UI-owned copy on every version change. The segment
        // Samples/RawMin/RawMax reference BeatSegmentCapture's publish pool,
        // which recycles a buffer two snapshots later; the interaction handlers
        // (range/view/absolute/strip selection) re-read _lastSnapshot with no new
        // frame, so caching the pooled snapshot would let them read a recycled
        // buffer. Copying the pooled arrays into owned storage keeps the Core
        // "read latest only" contract intact. Markers/Average are immutable
        // per their own contract and are shared as-is.
        if (snapshot != null && (snapshot.Version != _lastVersion || _lastSnapshot == null))
        {
            _lastSnapshot = CopyForCache(snapshot);
        }

        _lastCursorTimeS = context.ReviewCursorTimeS;
        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);
        if (snapshot == null || snapshot.Version == _lastVersion)
        {
            SetSignalQuality(_lastSnapshot?.Quality ?? SignalQualityFlags.None);
            if (cursorMoved)
            {
                _mainPlot.Refresh();
            }

            return;
        }

        _lastVersion = snapshot.Version;
        BeatSegmentsSnapshot cached = _lastSnapshot!;
        RenderMain(cached);
        RenderStrips(cached);
        RenderAverageView(cached);
        RefreshAll();
    }

    /// <summary>
    /// Deep-copies the pooled segment envelope/raw arrays into UI-owned storage
    /// so cached re-renders (interaction handlers, paused review) never read a
    /// segment buffer that the capture pool has since recycled. Scalar fields,
    /// markers, and the average snapshot are immutable and shared as-is.
    /// </summary>
    private static BeatSegmentsSnapshot CopyForCache(BeatSegmentsSnapshot snapshot)
    {
        IReadOnlyList<BeatSegment> segments = snapshot.Segments;
        if (segments.Count == 0)
        {
            return snapshot;
        }

        var owned = new BeatSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            BeatSegment s = segments[i];
            owned[i] = new BeatSegment
            {
                Samples = s.Samples.ToArray(),
                RawValid = s.RawValid,
                RawMin = s.RawMin.ToArray(),
                RawMax = s.RawMax.ToArray(),
                MsPerPoint = s.MsPerPoint,
                StartTimeS = s.StartTimeS,
                IsTic = s.IsTic,
                AOffsetMs = s.AOffsetMs,
                PeakValue = s.PeakValue,
                CPeakValid = s.CPeakValid,
                CPeakOffsetMs = s.CPeakOffsetMs,
                COnsetValid = s.COnsetValid,
                COnsetOffsetMs = s.COnsetOffsetMs,
                Quality = s.Quality,
            };
        }

        return new BeatSegmentsSnapshot
        {
            Version = snapshot.Version,
            Segments = owned,
            Markers = snapshot.Markers,
            LiftAngleDeg = snapshot.LiftAngleDeg,
            Average = snapshot.Average,
            Quality = snapshot.Quality,
        };
    }

    private VerticalLine GetOrCreateMarker(List<VerticalLine> pool, Plot plot, LinePattern pattern, uint colorArgb)
    {
        foreach (var marker in pool)
        {
            if (!marker.IsVisible) return marker;
        }

        var newMarker = plot.Add.VerticalLine(0.0);
        newMarker.LineWidth = 1;
        newMarker.LinePattern = pattern;
        newMarker.LineColor = ScottPlot.Color.FromARGB(colorArgb);
        newMarker.IsVisible = false;
        newMarker.EnableAutoscale = false;
        pool.Add(newMarker);
        return newMarker;
    }

    private void RenderMain(BeatSegmentsSnapshot snapshot)
    {
        StableStripWindow? displayedWindow = DisplayedWindow(snapshot, _selectedSlot, _rangeMs);
        BeatSegment? segment = displayedWindow?.Segment
            ?? (snapshot.Segments.Count > 0 ? snapshot.Segments[^1] : null);
        IReadOnlyList<BeatSegment> markerSegments = displayedWindow?.MarkerSegments ?? snapshot.Segments;
        _mainX.Clear();
        _mainY.Clear();
        _mainYMirror.Clear();
        _mainYLower = null;
        _mainYUpper = null;

        foreach (var m in _dynamicAMarkers) m.IsVisible = false;
        foreach (var m in _dynamicCPeakMarkers) m.IsVisible = false;

        if (segment == null)
        {
            SetMarker(_cOnsetMarker, null);
            SetSignalQuality(snapshot.Quality);
            return;
        }

        if (!_showAbsoluteValue && segment.RawValid)
        {
            RenderMainRaw(segment);
        }
        else
        {
            RenderMainEnvelope(segment);
        }

        double windowStartS = segment.StartTimeS;
        var cMarkerXs = new List<double>();
        foreach (BeatSegment other in markerSegments)
        {
            double? cOffsetMs = DisplayedCOffsetMs(other);
            if (cOffsetMs is double offsetMs)
            {
                double relC = (other.StartTimeS + offsetMs / 1000.0 - windowStartS) * 1000.0;
                if (relC >= 0.0 && relC <= _rangeMs
                    && !ContainsNearby(cMarkerXs, relC, Math.Max(1.0, segment.MsPerPoint)))
                {
                    AddMainMarker(BeatNoiseMarkerKind.CPeak, relC);
                    cMarkerXs.Add(relC);
                }
            }
        }

        if (snapshot.Markers.Count > 0)
        {
            foreach (BeatNoiseMarker eventMarker in snapshot.Markers)
            {
                double x = (eventMarker.TimeS - windowStartS) * 1000.0;
                if (eventMarker.Kind == BeatNoiseMarkerKind.A)
                {
                    AddMainMarker(eventMarker.Kind, x);
                }
                else if (IsDisplayedCMarker(eventMarker.Kind)
                    && x >= 0.0
                    && x <= _rangeMs
                    && !ContainsNearby(cMarkerXs, x, Math.Max(1.0, segment.MsPerPoint)))
                {
                    AddMainMarker(eventMarker.Kind, x);
                    cMarkerXs.Add(x);
                }
            }
        }
        else
        {
            foreach (BeatSegment other in markerSegments)
            {
                double relA = (other.StartTimeS + other.AOffsetMs / 1000.0 - windowStartS) * 1000.0;
                AddMainMarker(BeatNoiseMarkerKind.A, relA);
            }
        }

        SetMarker(_cOnsetMarker, null);
        SignalQualityFlags quality = snapshot.Quality | segment.Quality;
        if (cMarkerXs.Count == 0)
        {
            quality |= SignalQualityFlags.WeakSignal;
        }
        SetSignalQuality(quality);
    }

    private void RenderMainRaw(BeatSegment segment)
    {
        ReadOnlySpan<float> min = segment.RawMin.Span;
        ReadOnlySpan<float> max = segment.RawMax.Span;
        int count = Math.Min(DisplaySampleCount(segment), Math.Min(min.Length, max.Length));
        double rangeAbsMax = 0.0;
        for (int i = 0; i < count; i++)
        {
            double x = i * segment.MsPerPoint;
            _mainX.Add(x);
            _mainY.Add(max[i]);
            _mainYMirror.Add(min[i]);
            if (x <= _rangeMs)
            {
                double extent = Math.Max(Math.Abs(min[i]), Math.Abs(max[i]));
                if (extent > rangeAbsMax)
                {
                    rangeAbsMax = extent;
                }
            }
        }

        if (rangeAbsMax <= 0.0)
        {
            rangeAbsMax = 1.0;
        }

        double yMax = rangeAbsMax * 1.1;
        ExpandMainYLimits(-yMax, yMax);
    }

    private void RenderMainEnvelope(BeatSegment segment)
    {
        ReadOnlySpan<float> samples = segment.Samples.Span;
        int count = DisplaySampleCount(segment);
        double rangeMax = 0.0;
        for (int i = 0; i < count; i++)
        {
            double x = i * segment.MsPerPoint;
            double y = samples[i];
            _mainX.Add(x);
            _mainY.Add(y);
            _mainYMirror.Add(-y);
            if (x <= _rangeMs && y > rangeMax)
            {
                rangeMax = y;
            }
        }

        if (rangeMax <= 0.0)
        {
            rangeMax = 1.0;
        }

        double yMax = rangeMax * 1.1;
        ExpandMainYLimits(_showAbsoluteValue ? -0.02 * yMax : -yMax, yMax);
    }

    private int DisplaySampleCount(BeatSegment segment) => BeatNoiseScopeLogic.StripSampleCount(
        _viewMode, _rangeMs, segment.Samples.Length, segment.MsPerPoint);

    private void ExpandMainYLimits(double requiredLower, double requiredUpper)
    {
        if (_mainYLower is not double lower || _mainYUpper is not double upper)
        {
            _mainYLower = requiredLower;
            _mainYUpper = requiredUpper;
            _mainPlot.Plot.Axes.SetLimitsY(requiredLower, requiredUpper);
            return;
        }

        double nextLower = Math.Min(lower, requiredLower);
        double nextUpper = Math.Max(upper, requiredUpper);
        if (nextLower != lower || nextUpper != upper)
        {
            _mainYLower = nextLower;
            _mainYUpper = nextUpper;
            _mainPlot.Plot.Axes.SetLimitsY(nextLower, nextUpper);
        }
    }

    private void RenderStrips(BeatSegmentsSnapshot snapshot)
    {
        foreach (VerticalLine marker in _stripAMarkers) marker.IsVisible = false;
        foreach (VerticalLine marker in _stripCMarkers) marker.IsVisible = false;
        HideStripSlotLabels();

        for (int slot = 0; slot < BeatNoiseScopeLogic.StripCount; slot++)
        {
            List<double> x = _stripX[slot];
            List<double> y = _stripY[slot];
            x.Clear();
            y.Clear();

            IReadOnlyList<BeatSegment> markerSegments = snapshot.Segments;
            BeatSegment? segment;
            if (_viewMode == BeatNoiseScopeViewMode.EnvelopeAndStrip)
            {
                if (WindowForSlot(snapshot, slot, _rangeMs) is StableStripWindow window)
                {
                    segment = window.Segment;
                    markerSegments = window.MarkerSegments;
                }
                else
                {
                    segment = null;
                }
            }
            else
            {
                segment = SegmentForSlot(snapshot, slot);
            }

            if (segment == null)
            {
                continue;
            }

            ReadOnlySpan<float> sourceSpan = segment.Samples.Span;
            int sampleCount = BeatNoiseScopeLogic.StripSampleCount(
                _viewMode, _rangeMs, sourceSpan.Length, segment.MsPerPoint);
            if (sampleCount < sourceSpan.Length)
            {
                sourceSpan = sourceSpan.Slice(0, sampleCount);
            }

            double stripWindowStartMs = 0.0;
            if (_viewMode == BeatNoiseScopeViewMode.EnvelopeAndStrip
                && _rangeMs == DefaultRangeMs)
            {
                int halfSamples = BeatNoiseScopeLogic.StripSampleCount(
                    _viewMode, _rangeMs / 2, sourceSpan.Length, segment.MsPerPoint);
                if (slot % 2 == 1)
                {
                    int start = Math.Min(halfSamples, sourceSpan.Length);
                    sourceSpan = sourceSpan.Slice(start);
                    stripWindowStartMs = _rangeMs / 2.0;
                }
                else if (halfSamples < sourceSpan.Length)
                {
                    sourceSpan = sourceSpan.Slice(0, halfSamples);
                }
            }

            double renderedWindowMs = Math.Max(0.0, sourceSpan.Length * segment.MsPerPoint);
            RenderStripMarkers(slot, markerSegments, segment, stripWindowStartMs, renderedWindowMs);
            UpdateStripSlotLabel(slot, segment);

            // Compress each segment into its slot via the shared strip-lane
            // sampling policy (max-decimate + per-segment peak normalization).
            int stripSlot = slot;
            EnvelopeLaneSampler.MaxDecimateNormalized(
                sourceSpan, StripPointBudget,
                (p, points, _, normalized) =>
                {
                    x.Add(BeatNoiseScopeLogic.StripPointX(stripSlot, p, points));
                    y.Add(0.05 + 0.9 * normalized);
                });
        }

        UpdateSelectionSpan(snapshot);
    }

    private static Text AddStripSlotLabel(Plot plot)
    {
        Text label = plot.Add.Text(string.Empty, 0.0, 0.0);
        label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
        label.LabelBold = true;
        label.Alignment = Alignment.UpperCenter;
        label.LabelBorderColor = Colors.Transparent;
        label.LabelShadowColor = Colors.Transparent;
        label.IsVisible = false;
        return label;
    }

    private void HideStripSlotLabels()
    {
        foreach (Text? label in _stripSlotLabels)
        {
            if (label != null)
            {
                label.IsVisible = false;
            }
        }
    }

    private void UpdateStripSlotLabel(int slot, BeatSegment segment)
    {
        if (_viewMode != BeatNoiseScopeViewMode.AverageAndStrip
            || _stripSlotLabels[slot] is not { } label)
        {
            return;
        }

        label.IsVisible = true;
        label.LabelText = segment.IsTic ? "Tic" : "Toc";
        label.LabelFontColor = Color.FromARGB(segment.IsTic ? _theme.TraceTick : _theme.TraceTock);
        label.Location = new Coordinates(slot + 0.5, StripSlotLabelY);
    }

    private void UpdateSelectionSpan(BeatSegmentsSnapshot snapshot)
    {
        if (_selectionSpan == null)
        {
            return;
        }

        int? selectedSlot = SelectedOrDisplayedSlot(snapshot);
        bool visible = selectedSlot.HasValue;
        _selectionSpan.IsVisible = visible;
        if (visible && selectedSlot.HasValue)
        {
            int slot = selectedSlot.Value;
            int spanStart = _viewMode == BeatNoiseScopeViewMode.EnvelopeAndStrip && _rangeMs == DefaultRangeMs
                ? slot - slot % 2
                : slot;
            _selectionSpan.X1 = spanStart;
            _selectionSpan.X2 = _viewMode == BeatNoiseScopeViewMode.AverageAndStrip
                ? Math.Min(slot + 2, BeatNoiseScopeLogic.StripCount)
                : _rangeMs == DefaultRangeMs
                    ? Math.Min(spanStart + 2, BeatNoiseScopeLogic.StripCount)
                    : slot + 1;
        }
    }

    private int? SelectedOrDisplayedSlot(BeatSegmentsSnapshot snapshot)
    {
        if (_selectedSlot is int selectedSlot)
        {
            bool selectedSlotValid = _viewMode == BeatNoiseScopeViewMode.AverageAndStrip
                ? SegmentForSlot(snapshot, selectedSlot) != null || SegmentForSlot(snapshot, selectedSlot + 1) != null
                : WindowForSlot(snapshot, selectedSlot, _rangeMs) != null;
            if (selectedSlotValid)
            {
                return selectedSlot;
            }
        }

        return DisplayedStripSlot(snapshot);
    }

    private int? DisplayedStripSlot(BeatSegmentsSnapshot snapshot)
    {
        if (_viewMode == BeatNoiseScopeViewMode.AverageAndStrip
            || snapshot.Segments.Count == 0)
        {
            return null;
        }

        if (_rangeMs == DefaultRangeMs)
        {
            return StableNonOverlappingWindowSegments(snapshot, _rangeMs).Count > 0
                ? BeatNoiseScopeLogic.StripCount - 2
                : null;
        }

        int count = Math.Min(snapshot.Segments.Count, BeatNoiseScopeLogic.StripCount);
        return count > 0 ? BeatNoiseScopeLogic.StripCount - 1 : null;
    }

    private void RenderAverageView(BeatSegmentsSnapshot snapshot)
    {
        if (_viewMode == BeatNoiseScopeViewMode.AverageAndStrip
            && _selectedSlot is int slot)
        {
            ClearMilestoneLines();
            RenderSelectedAverageEnvelopePair(snapshot, slot);
            return;
        }

        RenderAverage(snapshot.Average);
    }

    private void RenderAverage(BeatNoiseAverageSnapshot average)
    {
        _averagePlot.Plot.Axes.SetLimits(0, BeatNoiseAverager.LaneWindowMs, -0.1, Lane1Baseline + 1.15);
        // One shared scale across both lanes so their relative amplitude shows.
        double max = 0.0;
        foreach (float value in average.Lane1)
        {
            if (value > max)
            {
                max = value;
            }
        }

        foreach (float value in average.Lane2)
        {
            if (value > max)
            {
                max = value;
            }
        }

        if (max <= 0.0)
        {
            max = 1.0;
        }

        RenderMilestones(average, max);
        FillLane(_lane1X, _lane1Y, average.Lane1, average.MsPerPoint, Lane1Baseline, max);
        FillLane(_lane2X, _lane2Y, average.Lane2, average.MsPerPoint, Lane2Baseline, max);
        UpdateAverageSignalLabels(average);
        _averageText.Text = BeatNoiseScopeLogic.AverageLine(average);
    }

    private static Text AddAverageSignalLabel(Plot plot)
    {
        Text label = plot.Add.Text(string.Empty, 0.0, 0.0);
        label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
        label.LabelBold = true;
        label.Alignment = Alignment.UpperLeft;
        label.LabelBackgroundColor = Colors.Transparent;
        label.LabelBorderColor = Colors.Transparent;
        label.LabelShadowColor = Colors.Transparent;
        label.IsVisible = false;
        return label;
    }

    private void UpdateAverageSignalLabels(BeatNoiseAverageSnapshot average)
    {
        SetAverageSignalLabel(_lane1SignalLabel, "Tic", average.Lane1Count, average.Lane1MeanPeak, Lane1Baseline + 1.05);
        SetAverageSignalLabel(_lane2SignalLabel, "Toc", average.Lane2Count, average.Lane2MeanPeak, Lane2Baseline + 1.05);
    }

    private static void SetAverageSignalLabel(Text? label, string traceName, int count, double meanPeak, double y)
    {
        if (label == null)
        {
            return;
        }

        label.IsVisible = count > 0;
        if (!label.IsVisible)
        {
            return;
        }

        label.LabelText = traceName + " Signal Level " + meanPeak.ToString("0.000", CultureInfo.InvariantCulture);
        label.Location = new Coordinates(0.4, y);
    }

    private void RenderSelectedAverageEnvelopePair(BeatSegmentsSnapshot snapshot, int pairStartSlot)
    {
        _lane1X.Clear();
        _lane1Y.Clear();
        _lane2X.Clear();
        _lane2Y.Clear();
        HideAverageSignalLabels();

        BeatSegment? trace1 = SegmentForSlot(snapshot, pairStartSlot);
        BeatSegment? trace2 = SegmentForSlot(snapshot, pairStartSlot + 1);
        double max = Math.Max(MaxSegmentValue(trace1), MaxSegmentValue(trace2));
        if (max <= 0.0)
        {
            max = 1.0;
        }

        if (trace1 != null)
        {
            FillSelectedLane(_lane1X, _lane1Y, trace1, Lane1Baseline, max);
        }

        if (trace2 != null)
        {
            FillSelectedLane(_lane2X, _lane2Y, trace2, Lane2Baseline, max);
        }

        _averageText.Text = "Tic selected · Toc selected";
        _averagePlot.Plot.Axes.SetLimits(0, BeatNoiseAverager.LaneWindowMs, -0.1, Lane1Baseline + 1.15);
    }

    private void HideAverageSignalLabels()
    {
        if (_lane1SignalLabel != null)
        {
            _lane1SignalLabel.IsVisible = false;
        }

        if (_lane2SignalLabel != null)
        {
            _lane2SignalLabel.IsVisible = false;
        }
    }

    private void AddMainMarker(BeatNoiseMarkerKind kind, double x)
    {
        if (x < 0.0 || x > _rangeMs)
        {
            return;
        }

        List<VerticalLine> pool = kind == BeatNoiseMarkerKind.A
            ? _dynamicAMarkers
            : _dynamicCPeakMarkers;
        LinePattern pattern = GraphLinePatterns.VerticalGuide;
        uint color = kind == BeatNoiseMarkerKind.A
            ? _theme.TraceTick
            : _theme.TraceTock;
        VerticalLine marker = GetOrCreateMarker(pool, _mainPlot.Plot, pattern, color);
        marker.X = x;
        marker.IsVisible = true;
    }

    private bool IsDisplayedCMarker(BeatNoiseMarkerKind kind)
    {
        return _useCOnset
            ? kind == BeatNoiseMarkerKind.COnset
            : kind == BeatNoiseMarkerKind.CPeak;
    }

    private static bool ContainsNearby(List<double> values, double x, double tolerance)
    {
        foreach (double value in values)
        {
            if (Math.Abs(value - x) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static BeatSegment? SegmentForSlot(BeatSegmentsSnapshot snapshot, int slot)
    {
        if (slot < 0 || slot >= BeatNoiseScopeLogic.StripCount)
        {
            return null;
        }

        int pairStart = slot - slot % 2;
        BeatSegment? older = ChronologicalSegmentForSlot(snapshot, pairStart);
        BeatSegment? newer = ChronologicalSegmentForSlot(snapshot, pairStart + 1);
        bool wantsTic = slot % 2 == 0;
        if (wantsTic)
        {
            return newer is { IsTic: true } ? newer
                : older is { IsTic: true } ? older
                : null;
        }

        return newer is { IsTic: false } ? newer
            : older is { IsTic: false } ? older
            : null;
    }

    private static BeatSegment? ChronologicalSegmentForSlot(BeatSegmentsSnapshot snapshot, int slot)
    {
        int index = BeatNoiseScopeLogic.SegmentIndexForSlot(slot, snapshot.Segments.Count);
        return index >= 0 ? snapshot.Segments[index] : null;
    }

    private BeatSegment? DisplayedSegment(BeatSegmentsSnapshot snapshot, int? selectedSlot, int rangeMs)
    {
        if (snapshot.Segments.Count == 0)
        {
            return null;
        }

        if (DisplayedWindow(snapshot, selectedSlot, rangeMs)?.Segment is { } segment)
        {
            return segment;
        }

        return snapshot.Segments[^1];
    }

    private StableStripWindow? DisplayedWindow(BeatSegmentsSnapshot snapshot, int? selectedSlot, int rangeMs)
    {
        if (snapshot.Segments.Count == 0)
        {
            return null;
        }

        if (selectedSlot is int slot && WindowForSlot(snapshot, slot, rangeMs) is { } selectedWindow)
        {
            return selectedWindow;
        }

        if (rangeMs == DefaultRangeMs)
        {
            IReadOnlyList<StableStripWindow> windows = StableNonOverlappingWindowSegments(snapshot, rangeMs);
            if (windows.Count > 0)
            {
                return windows[^1];
            }
        }

        return null;
    }

    private StableStripWindow? WindowForSlot(BeatSegmentsSnapshot snapshot, int slot, int rangeMs)
    {
        if (slot < 0 || slot >= BeatNoiseScopeLogic.StripCount)
        {
            return null;
        }

        if (rangeMs != DefaultRangeMs)
        {
            int count = Math.Min(snapshot.Segments.Count, BeatNoiseScopeLogic.StripCount);
            int index = slot - (BeatNoiseScopeLogic.StripCount - count);
            if (index < 0 || index >= count)
            {
                return null;
            }

            BeatSegment segment = snapshot.Segments[snapshot.Segments.Count - count + index];
            return new StableStripWindow(WindowBucket(segment, rangeMs), segment, snapshot.Segments);
        }

        IReadOnlyList<StableStripWindow> windows = StableNonOverlappingWindowSegments(snapshot, rangeMs);
        int effectiveSlot = slot / 2;
        int stripWindowCount = BeatNoiseScopeLogic.StripCount / 2;
        int windowIndex = effectiveSlot - (stripWindowCount - windows.Count);
        return windowIndex >= 0 && windowIndex < windows.Count ? windows[windowIndex] : null;
    }

    private IReadOnlyList<StableStripWindow> StableNonOverlappingWindowSegments(BeatSegmentsSnapshot snapshot, int rangeMs)
    {
        if (snapshot.Segments.Count == 0)
        {
            _stable400MsStripWindows.Clear();
            return _stable400MsStripWindows;
        }

        int maxWindows = BeatNoiseScopeLogic.StripCount / 2;
        long newestBucket = WindowBucket(snapshot.Segments[^1], rangeMs);
        long oldestVisibleBucket = newestBucket - maxWindows + 1;
        _stable400MsStripWindows.RemoveAll(window =>
            window.Bucket < oldestVisibleBucket || window.Bucket > newestBucket);

        foreach (BeatSegment segment in snapshot.Segments)
        {
            long bucket = WindowBucket(segment, rangeMs);
            if (bucket < oldestVisibleBucket || bucket > newestBucket)
            {
                continue;
            }

            if (_stable400MsStripWindows.Any(window => window.Bucket == bucket))
            {
                _stable400MsStripWindows
                    .First(window => window.Bucket == bucket)
                    .MergeMarkerSegments(snapshot.Segments);
                continue;
            }

            _stable400MsStripWindows.Add(new StableStripWindow(
                bucket,
                segment,
                snapshot.Segments.ToArray()));
        }

        _stable400MsStripWindows.Sort((left, right) => left.Bucket.CompareTo(right.Bucket));
        return _stable400MsStripWindows;
    }

    private static long WindowBucket(BeatSegment segment, int rangeMs)
    {
        double beatTimeMs = (segment.StartTimeS * 1000.0) + segment.AOffsetMs;
        return (long)Math.Floor(beatTimeMs / rangeMs);
    }

    private static double MaxSegmentValue(BeatSegment? segment)
    {
        if (segment == null)
        {
            return 0.0;
        }

        ReadOnlySpan<float> samples = SelectedAverageSamples(segment);
        double max = 0.0;
        foreach (float value in samples)
        {
            if (value > max)
            {
                max = value;
            }
        }

        return max;
    }

    private static void FillSelectedLane(List<double> x, List<double> y, BeatSegment segment, double baseline, double scale)
    {
        ReadOnlySpan<float> samples = SelectedAverageSamples(segment);
        for (int i = 0; i < samples.Length; i++)
        {
            x.Add(i * segment.MsPerPoint);
            y.Add(baseline + samples[i] / scale);
        }
    }

    private static ReadOnlySpan<float> SelectedAverageSamples(BeatSegment segment)
    {
        ReadOnlySpan<float> samples = segment.Samples.Span;
        int sampleCount = BeatNoiseScopeLogic.StripSampleCount(
            BeatNoiseScopeViewMode.AverageAndStrip, DefaultRangeMs, samples.Length, segment.MsPerPoint);
        return sampleCount < samples.Length ? samples.Slice(0, sampleCount) : samples;
    }

    private void RenderMilestones(BeatNoiseAverageSnapshot average, double scale)
    {
        EnsureMilestoneScatters();
        ClearMilestoneLines();
        for (int i = 0; i < average.Milestones.Count && i < 5; i++)
        {
            BeatNoiseAverageMilestone milestone = average.Milestones[i];
            FillLane(_lane1MilestoneX[i], _lane1MilestoneY[i], milestone.Lane1, average.MsPerPoint, Lane1Baseline, scale);
            FillLane(_lane2MilestoneX[i], _lane2MilestoneY[i], milestone.Lane2, average.MsPerPoint, Lane2Baseline, scale);
            if (_lane1MilestoneScatters[i] != null)
            {
                _lane1MilestoneScatters[i]!.IsVisible = milestone.Lane1.Count > 0;
            }

            if (_lane2MilestoneScatters[i] != null)
            {
                _lane2MilestoneScatters[i]!.IsVisible = milestone.Lane2.Count > 0;
            }
        }
    }

    private void EnsureMilestoneScatters()
    {
        for (int i = 0; i < 5; i++)
        {
            if (_lane1MilestoneScatters[i] == null)
            {
                Scatter s1 = _averagePlot.Plot.Add.Scatter(_lane1MilestoneX[i], _lane1MilestoneY[i]);
                s1.LineWidth = 1;
                s1.MarkerStyle.IsVisible = false;
                s1.LegendText = string.Empty;
                s1.IsVisible = false;
                _lane1MilestoneScatters[i] = s1;
            }

            if (_lane2MilestoneScatters[i] == null)
            {
                Scatter s2 = _averagePlot.Plot.Add.Scatter(_lane2MilestoneX[i], _lane2MilestoneY[i]);
                s2.LineWidth = 1;
                s2.MarkerStyle.IsVisible = false;
                s2.LegendText = string.Empty;
                s2.IsVisible = false;
                _lane2MilestoneScatters[i] = s2;
            }
        }

        ApplySeriesTheme();
    }

    private static void FillLane(
        List<double> x, List<double> y, IReadOnlyList<float> lane,
        double msPerPoint, double baseline, double scale)
    {
        x.Clear();
        y.Clear();
        for (int i = 0; i < lane.Count; i++)
        {
            x.Add(i * msPerPoint);
            y.Add(baseline + lane[i] / scale);
        }
    }

    private static int? NextAveragePairSelection(int? currentSlot, BeatSegmentsSnapshot snapshot, int clickedSlot)
    {
        int? phaseStart = PhaseAlignedPairStartSlot(snapshot, clickedSlot);
        if (phaseStart is not int pairStartSlot)
        {
            return null;
        }

        return currentSlot == pairStartSlot ? null : pairStartSlot;
    }

    private static int? NextDisplaySelection(int? currentSlot, BeatSegmentsSnapshot snapshot, int clickedSlot)
    {
        if (SegmentForSlot(snapshot, clickedSlot) == null)
        {
            return null;
        }

        return currentSlot == clickedSlot ? null : clickedSlot;
    }

    private int? NextBeatScopeSelection(
        int? currentSlot,
        BeatSegmentsSnapshot snapshot,
        int clickedSlot,
        int rangeMs)
    {
        return NextWindowSelection(currentSlot, snapshot, clickedSlot, rangeMs);
    }

    private int? NextWindowSelection(
        int? currentSlot,
        BeatSegmentsSnapshot snapshot,
        int clickedSlot,
        int rangeMs)
    {
        if (WindowForSlot(snapshot, clickedSlot, rangeMs) == null)
        {
            return null;
        }

        return currentSlot == clickedSlot ? null : clickedSlot;
    }

    private static int? PhaseAlignedPairStartSlot(BeatSegmentsSnapshot snapshot, int clickedSlot)
    {
        BeatSegment? clicked = SegmentForSlot(snapshot, clickedSlot);
        if (clicked == null)
        {
            return null;
        }

        if (clicked.IsTic)
        {
            return clickedSlot;
        }

        if (SegmentForSlot(snapshot, clickedSlot - 1) is { IsTic: true })
        {
            return clickedSlot - 1;
        }

        if (SegmentForSlot(snapshot, clickedSlot + 1) is { IsTic: true })
        {
            return clickedSlot + 1;
        }

        return clickedSlot - clickedSlot % 2;
    }

    private void RenderStripMarkers(
        int slot,
        IReadOnlyList<BeatSegment> markerSegments,
        BeatSegment segment,
        double windowStartMs,
        double windowMs)
    {
        if (windowMs <= 0.0)
        {
            return;
        }

        foreach (BeatSegment other in markerSegments)
        {
            double relativeAOffsetMs = (other.StartTimeS + other.AOffsetMs / 1000.0 - segment.StartTimeS) * 1000.0;
            double aInWindowMs = relativeAOffsetMs - windowStartMs;
            if (aInWindowMs >= 0.0 && aInWindowMs <= windowMs)
            {
                AddStripMarker(_stripAMarkers, GraphLinePatterns.VerticalGuide, _theme.TraceTick,
                    BeatNoiseScopeLogic.StripMarkerX(slot, aInWindowMs, windowMs));
            }

            double? cOffsetMs = DisplayedCOffsetMs(other);
            if (cOffsetMs is double offsetMs)
            {
                double relativeCOffsetMs = (other.StartTimeS + offsetMs / 1000.0 - segment.StartTimeS) * 1000.0;
                double cInWindowMs = relativeCOffsetMs - windowStartMs;
                if (cInWindowMs >= 0.0 && cInWindowMs <= windowMs)
                {
                    AddStripMarker(_stripCMarkers, GraphLinePatterns.VerticalGuide, _theme.TraceTock,
                        BeatNoiseScopeLogic.StripMarkerX(slot, cInWindowMs, windowMs));
                }
            }
        }
    }

    private void AddStripMarker(List<VerticalLine> pool, LinePattern pattern, uint colorArgb, double x)
    {
        VerticalLine marker = GetOrCreateMarker(pool, _stripPlot.Plot, pattern, colorArgb);
        marker.X = x;
        marker.IsVisible = true;
    }

    /// <summary>Review-cursor contract: a vertical marker at the scrub time's in-window offset.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        if (_reviewCursor == null)
        {
            return false;
        }

        BeatSegment? segment = _lastSnapshot is { } snapshot
            ? DisplayedSegment(snapshot, _selectedSlot, _rangeMs)
            : null;
        double? offsetMs = BeatNoiseScopeLogic.CursorOffsetMs(reviewCursorTimeS, segment);
        return _reviewCursor.Update(offsetMs);
    }

    private void ApplyRangeLimits()
    {
        _mainPlot.Plot.Axes.SetLimitsX(0, _rangeMs);
    }

    private static VerticalLine AddMarker(Plot plot, LinePattern pattern)
    {
        VerticalLine marker = plot.Add.VerticalLine(0.0);
        marker.LineWidth = 1;
        marker.LinePattern = pattern;
        marker.IsVisible = false;
        marker.EnableAutoscale = false;
        return marker;
    }

    private double? DisplayedCOffsetMs(BeatSegment segment)
    {
        if (_useCOnset)
        {
            return segment.COnsetValid ? segment.COnsetOffsetMs : null;
        }

        if (!segment.CPeakValid)
        {
            return null;
        }

        if (!_showAbsoluteValue && segment.RawValid && segment.MsPerPoint > 0.0)
        {
            return Math.Floor(segment.CPeakOffsetMs / segment.MsPerPoint) * segment.MsPerPoint;
        }

        return segment.CPeakOffsetMs;
    }

    private Text AddWeakSignalLabel(Plot plot)
    {
        Text label = plot.Add.Text("WEAK SIGNAL", 0.0, 0.0);
        label.LabelFontSize = PlotThemeHelper.GraphLabelFontSize;
        label.LabelBold = true;
        label.Alignment = Alignment.UpperRight;
        label.IsVisible = false;
        return label;
    }

    private void SetSignalQuality(SignalQualityFlags quality)
    {
        _ = quality;
        if (_weakSignalLabel != null)
        {
            _weakSignalLabel.IsVisible = false;
        }
    }

    private static void SetMarker(VerticalLine? marker, double? x)
    {
        if (marker == null)
        {
            return;
        }

        marker.IsVisible = x is not null;
        if (x is double position)
        {
            marker.X = position;
        }
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private void ApplySeriesTheme()
    {
        if (_mainScatter != null)
        {
            _mainScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        if (_mirrorScatter != null)
        {
            _mirrorScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        if (_mainALegendScatter != null)
        {
            _mainALegendScatter.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_mainCLegendScatter != null)
        {
            _mainCLegendScatter.LineColor = Color.FromARGB(_theme.TraceTock);
        }

        // A = Tic green, C = Toc red: the same themed event color mapping the
        // scope markers use (RateScopeRenderer.ThemeColor).
        foreach (var marker in _dynamicAMarkers)
        {
            marker.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        foreach (var marker in _dynamicCPeakMarkers)
        {
            marker.LineColor = Color.FromARGB(_theme.TraceTock);
        }

        if (_cOnsetMarker != null)
        {
            _cOnsetMarker.LineColor = Color.FromARGB(_theme.TraceTock);
        }

        if (_weakSignalLabel != null)
        {
            _weakSignalLabel.LabelFontColor = Color.FromARGB(_theme.VarioWarn);
        }

        foreach (Scatter? strip in _stripScatters)
        {
            if (strip != null)
            {
                strip.LineColor = Color.FromARGB(_theme.TraceWave);
            }
        }

        foreach (VerticalLine? divider in _stripDividers)
        {
            if (divider != null)
            {
                divider.LineColor = Color.FromARGB(_theme.TextPrimary).WithAlpha(StripDividerAlpha);
            }
        }

        foreach (VerticalLine marker in _stripAMarkers)
        {
            marker.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        foreach (VerticalLine marker in _stripCMarkers)
        {
            marker.LineColor = Color.FromARGB(_theme.TraceTock);
        }

        foreach (Text? label in _stripSlotLabels)
        {
            if (label == null)
            {
                continue;
            }

            label.LabelFontColor = Color.FromARGB(label.LabelText == "Toc" ? _theme.TraceTock : _theme.TraceTick);
            label.LabelBackgroundColor = Color.FromARGB(_theme.ScopeBg).WithAlpha(210);
            label.LabelBorderColor = Colors.Transparent;
            label.LabelShadowColor = Colors.Transparent;
        }

        if (_selectionSpan != null)
        {
            _selectionSpan.FillStyle.Color = Color.FromARGB(_theme.TraceTick).WithAlpha(SelectionFillAlpha);
            _selectionSpan.LineStyle.Color = Color.FromARGB(_theme.TraceTick);
        }

        // Lane colors follow the Tic/Toc phase labels.
        if (_lane1Scatter != null)
        {
            _lane1Scatter.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_lane2Scatter != null)
        {
            _lane2Scatter.LineColor = Color.FromARGB(_theme.TraceTock);
        }

        if (_lane1SignalLabel != null)
        {
            _lane1SignalLabel.LabelFontColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_lane2SignalLabel != null)
        {
            _lane2SignalLabel.LabelFontColor = Color.FromARGB(_theme.TraceTock);
        }

        for (int i = 0; i < 5; i++)
        {
            byte alpha = (byte)(55 + (i * 25));
            if (_lane1MilestoneScatters[i] != null)
            {
                _lane1MilestoneScatters[i]!.LineColor = Color.FromARGB(_theme.TraceTick).WithAlpha(alpha);
            }
            if (_lane2MilestoneScatters[i] != null)
            {
                _lane2MilestoneScatters[i]!.LineColor = Color.FromARGB(_theme.TraceTock).WithAlpha(alpha);
            }
        }

        _averagePlot.Plot.Legend.BackgroundColor = Color.FromARGB(_theme.ScopeBg);
        _averagePlot.Plot.Legend.FontColor = Color.FromARGB(_theme.TextPrimary);
        _averagePlot.Plot.Legend.OutlineColor = Color.FromARGB(_theme.ScopeGrid);
        _mainPlot.Plot.Legend.BackgroundColor = Color.FromARGB(_theme.ScopeBg);
        _mainPlot.Plot.Legend.FontColor = Color.FromARGB(_theme.TextPrimary);
        _mainPlot.Plot.Legend.OutlineColor = Color.FromARGB(_theme.ScopeGrid);

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

    private void RefreshAll()
    {
        _mainPlot.Refresh();
        _stripPlot.Refresh();
        _averagePlot.Refresh();
    }

    /// <summary>
    /// Caps zoom-out and pan on a scope plot to its initial full-output extent on
    /// both axes: the view can still zoom in and pan within, but never past
    /// [xMin, xMax] x [yMin, yMax]. The bounds are read live each render via the
    /// providers, so a range-button change (X) or a running Y auto-fit grow is
    /// honored. A null Y bound (before the first render) leaves Y unconstrained.
    /// </summary>
    private sealed class ScopeViewBoundsRule : IAxisRule
    {
        private readonly IXAxis _xAxis;
        private readonly IYAxis _yAxis;
        private readonly Func<(double Min, double Max)> _xBounds;
        private readonly Func<(double Min, double Max)?> _yBounds;

        public ScopeViewBoundsRule(
            IXAxis xAxis,
            IYAxis yAxis,
            Func<(double Min, double Max)> xBounds,
            Func<(double Min, double Max)?> yBounds)
        {
            _xAxis = xAxis;
            _yAxis = yAxis;
            _xBounds = xBounds;
            _yBounds = yBounds;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            (double xMin, double xMax) = _xBounds();
            ClampAxis(_xAxis, xMin, xMax);
            if (_yBounds() is (double yMin, double yMax))
            {
                ClampAxis(_yAxis, yMin, yMax);
            }
        }

        /// <summary>
        /// Holds the axis view inside [min, max]: a span at or past the full
        /// extent snaps to it (zoom-out cap); otherwise the window is shifted
        /// back inside the bound (pan cap). Mirrors the X-bounds rules used by
        /// the other scopes.
        /// </summary>
        private static void ClampAxis(IAxis axis, double min, double max)
        {
            double extent = max - min;
            if (extent <= 0.0)
            {
                return;
            }

            double lo = axis.Range.Min;
            double hi = axis.Range.Max;
            double span = hi - lo;
            if (span >= extent)
            {
                lo = min;
                hi = max;
            }
            else
            {
                if (lo < min)
                {
                    lo = min;
                    hi = min + span;
                }

                if (hi > max)
                {
                    hi = max;
                    lo = max - span;
                }

                if (lo < min)
                {
                    lo = min;
                }
            }

            axis.Range.Min = lo;
            axis.Range.Max = hi;
        }
    }
}
