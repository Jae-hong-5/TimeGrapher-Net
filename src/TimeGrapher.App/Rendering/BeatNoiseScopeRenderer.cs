using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Beat-Noise Scope (Scope 1 + Scope 2), rendered from the cumulative
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
/// vertically offset and labeled trace 1/2 (never tic/toc), with the per-lane
/// average signal level and Σ progress in the readout below.
///
/// All plottables refill in place; re-renders only when the snapshot version
/// changes, so coalesced or repeated frames cost nothing. Segments reference
/// pooled Core buffers that stay valid only until rotated out, so every render
/// re-reads from the latest snapshot and nothing UI-side caches sample data
/// beyond it.
/// </summary>
internal sealed class BeatNoiseScopeRenderer
{
    public const int DefaultRangeMs = 400;

    private const int StripPointBudget = 200;
    private const float StripLeftAxisSizePx = 50.0f;
    private const byte StripDividerAlpha = 140;
    private const double Lane2Baseline = 0.0;
    private const double Lane1Baseline = 1.2;
    private const double MinimumValidCSeparationMs = 4.0;
    private const double MinimumUsablePeakValue = 0.12;
    private const byte SelectionFillAlpha = 48;

    private readonly AvaPlot _mainPlot;
    private readonly AvaPlot _stripPlot;
    private readonly AvaPlot _averagePlot;
    private readonly TextBlock _liftText;
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

    private Scatter? _mainScatter;
    private Scatter? _mirrorScatter;
    private readonly List<VerticalLine> _dynamicAMarkers = new();
    private readonly List<VerticalLine> _dynamicCPeakMarkers = new();
    private VerticalLine? _cOnsetMarker;
    private Text? _weakSignalLabel;
    private ReviewCursorLayer? _reviewCursor;
    private readonly Scatter?[] _stripScatters;
    private readonly VerticalLine?[] _stripDividers;
    private readonly VerticalLine?[] _stripAMarkers;
    private readonly VerticalLine?[] _stripCMarkers;
    private HorizontalSpan? _selectionSpan;
    private Scatter? _lane1Scatter;
    private Scatter? _lane2Scatter;
    private readonly List<double>[] _lane1MilestoneX = new List<double>[5];
    private readonly List<double>[] _lane1MilestoneY = new List<double>[5];
    private readonly List<double>[] _lane2MilestoneX = new List<double>[5];
    private readonly List<double>[] _lane2MilestoneY = new List<double>[5];
    private readonly Scatter?[] _lane1MilestoneScatters = new Scatter?[5];
    private readonly Scatter?[] _lane2MilestoneScatters = new Scatter?[5];

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private int _rangeMs = DefaultRangeMs;
    private bool _showAbsoluteValue;
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
        TextBlock liftText,
        TextBlock averageText)
    {
        _mainPlot = mainPlot;
        _stripPlot = stripPlot;
        _averagePlot = averagePlot;
        _liftText = liftText;
        _averageText = averageText;

        _stripX = new List<double>[BeatNoiseScopeLogic.StripCount];
        _stripY = new List<double>[BeatNoiseScopeLogic.StripCount];
        _stripScatters = new Scatter?[BeatNoiseScopeLogic.StripCount];
        _stripDividers = new VerticalLine?[BeatNoiseScopeLogic.StripCount - 1];
        _stripAMarkers = new VerticalLine?[BeatNoiseScopeLogic.StripCount];
        _stripCMarkers = new VerticalLine?[BeatNoiseScopeLogic.StripCount];
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

        _stripPlot.UserInputProcessor.Disable();
    }

    public int RangeMs => _rangeMs;

    public BeatNoiseScopeViewMode ViewMode => _viewMode;

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
        ClearMilestoneLines();
        _liftText.Text = "LIFT —";
        _averageText.Text = BeatNoiseScopeLogic.AverageLine(BeatNoiseAverageSnapshot.Empty);

        Plot main = _mainPlot.Plot;
        main.Clear();
        _mainX.Clear();
        _mainY.Clear();
        _mainYMirror.Clear();
        ApplyPlotTheme(main);
        main.Axes.Left.TickLabelStyle.IsVisible = false;
        main.XLabel("ms");
        _mainScatter = main.Add.Scatter(_mainX, _mainY);
        _mainScatter.LineWidth = 1;
        _mainScatter.MarkerStyle.IsVisible = false;
        _mirrorScatter = main.Add.Scatter(_mainX, _mainYMirror);
        _mirrorScatter.LineWidth = 1;
        _mirrorScatter.MarkerStyle.IsVisible = false;
        _mirrorScatter.IsVisible = !_showAbsoluteValue;
        _dynamicAMarkers.Clear();
        _dynamicCPeakMarkers.Clear();
        _cOnsetMarker = AddMarker(main, LinePattern.Dotted);
        _weakSignalLabel = AddWeakSignalLabel(main);
        _reviewCursor = AddCursor(main);
        ApplyRangeLimits();
        PlotAxisRules.ClampLeftEdgeToZero(main);
        main.Axes.Left.MinimumSize = StripLeftAxisSizePx;
        main.Axes.Left.MaximumSize = StripLeftAxisSizePx;

        // Keep the strip visually minimal while reserving the same left axis
        // width as the plot above it, so the data areas line up.
        Plot strip = _stripPlot.Plot;
        strip.Clear();
        ApplyPlotTheme(strip);
        strip.XLabel("ms");
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
            _stripAMarkers[i] = strip.Add.VerticalLine(0.0);
            _stripAMarkers[i]!.LineWidth = 1;
            _stripAMarkers[i]!.LinePattern = LinePattern.Dashed;
            _stripAMarkers[i]!.IsVisible = false;
            _stripAMarkers[i]!.EnableAutoscale = false;
            _stripCMarkers[i] = strip.Add.VerticalLine(0.0);
            _stripCMarkers[i]!.LineWidth = 1;
            _stripCMarkers[i]!.LinePattern = LinePattern.Dotted;
            _stripCMarkers[i]!.IsVisible = false;
            _stripCMarkers[i]!.EnableAutoscale = false;
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
        average.Axes.Left.TickLabelStyle.IsVisible = false;

        _lane1Scatter = average.Add.Scatter(_lane1X, _lane1Y);
        _lane1Scatter.LineWidth = 1;
        _lane1Scatter.MarkerStyle.IsVisible = false;
        _lane1Scatter.LegendText = "Trace 1";
        _lane2Scatter = average.Add.Scatter(_lane2X, _lane2Y);
        _lane2Scatter.LineWidth = 1;
        _lane2Scatter.MarkerStyle.IsVisible = false;
        _lane2Scatter.LegendText = "Trace 2";
        average.ShowLegend();
        average.Axes.SetLimits(0, BeatNoiseAverager.LaneWindowMs, -0.1, Lane1Baseline + 1.15);
        PlotAxisRules.ClampLeftEdgeToZero(average);
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
            ? NextAveragePairSelection(_selectedSlot, slot, snapshot.Segments.Count)
            : BeatNoiseScopeLogic.NextSelection(_selectedSlot, slot, snapshot.Segments.Count);
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
        _liftText.Text = BeatNoiseScopeLogic.LiftText(cached.LiftAngleDeg);
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
            };
        }

        return new BeatSegmentsSnapshot
        {
            Version = snapshot.Version,
            Segments = owned,
            Markers = snapshot.Markers,
            LiftAngleDeg = snapshot.LiftAngleDeg,
            Average = snapshot.Average,
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
        BeatSegment? segment = BeatNoiseScopeLogic.DisplayedSegment(snapshot, _selectedSlot);
        _mainX.Clear();
        _mainY.Clear();
        _mainYMirror.Clear();

        foreach (var m in _dynamicAMarkers) m.IsVisible = false;
        foreach (var m in _dynamicCPeakMarkers) m.IsVisible = false;

        if (segment == null)
        {
            SetMarker(_cOnsetMarker, null);
            SetWeakSignalVisible(true);
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
        if (snapshot.Markers.Count > 0)
        {
            foreach (BeatNoiseMarker eventMarker in snapshot.Markers)
            {
                double x = (eventMarker.TimeS - windowStartS) * 1000.0;
                AddMainMarker(eventMarker.Kind, x);
            }
        }
        else
        {
            foreach (BeatSegment other in snapshot.Segments)
            {
                double relA = (other.StartTimeS + other.AOffsetMs / 1000.0 - windowStartS) * 1000.0;
                AddMainMarker(BeatNoiseMarkerKind.A, relA);
                if (other.CPeakValid)
                {
                    double relC = (other.StartTimeS + other.CPeakOffsetMs / 1000.0 - windowStartS) * 1000.0;
                    AddMainMarker(BeatNoiseMarkerKind.CPeak, relC);
                }
            }
        }

        SetMarker(_cOnsetMarker, segment.COnsetValid ? segment.COnsetOffsetMs : null);
        SetWeakSignalVisible(!HasUsableCMarker(segment));
    }

    private void RenderMainRaw(BeatSegment segment)
    {
        ReadOnlySpan<float> min = segment.RawMin.Span;
        ReadOnlySpan<float> max = segment.RawMax.Span;
        int count = Math.Min(min.Length, max.Length);
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
        double rangeMax = 0.0;
        for (int i = 0; i < samples.Length; i++)
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
        for (int slot = 0; slot < BeatNoiseScopeLogic.StripCount; slot++)
        {
            List<double> x = _stripX[slot];
            List<double> y = _stripY[slot];
            x.Clear();
            y.Clear();

            int index = BeatNoiseScopeLogic.SegmentIndexForSlot(slot, snapshot.Segments.Count);
            if (index < 0)
            {
                SetStripAMarker(slot, null, 0.0);
                SetStripCMarker(slot, null, 0.0);
                continue;
            }

            BeatSegment segment = snapshot.Segments[index];
            ReadOnlySpan<float> sourceSpan = segment.Samples.Span;
            int sampleCount = BeatNoiseScopeLogic.StripSampleCount(
                _viewMode, _rangeMs, sourceSpan.Length, segment.MsPerPoint);
            if (sampleCount < sourceSpan.Length)
            {
                sourceSpan = sourceSpan.Slice(0, sampleCount);
            }

            SetStripCMarker(slot, segment, sampleCount * segment.MsPerPoint);
            SetStripAMarker(slot, segment, sampleCount * segment.MsPerPoint);

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

    private void UpdateSelectionSpan(BeatSegmentsSnapshot snapshot)
    {
        if (_selectionSpan == null)
        {
            return;
        }

        int? selectedSlot = _selectedSlot;
        bool visible = selectedSlot.HasValue && (_viewMode == BeatNoiseScopeViewMode.AverageAndStrip
            ? SegmentForSlot(snapshot, selectedSlot.Value) != null || SegmentForSlot(snapshot, selectedSlot.Value + 1) != null
            : BeatNoiseScopeLogic.SegmentIndexForSlot(selectedSlot.Value, snapshot.Segments.Count) >= 0);
        _selectionSpan.IsVisible = visible;
        if (visible && selectedSlot.HasValue)
        {
            int slot = selectedSlot.Value;
            _selectionSpan.X1 = slot;
            _selectionSpan.X2 = _viewMode == BeatNoiseScopeViewMode.AverageAndStrip
                ? Math.Min(slot + 2, BeatNoiseScopeLogic.StripCount)
                : slot + 1;
        }
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
        _averageText.Text = BeatNoiseScopeLogic.AverageLine(average);
    }

    private void RenderSelectedAverageEnvelopePair(BeatSegmentsSnapshot snapshot, int pairStartSlot)
    {
        _lane1X.Clear();
        _lane1Y.Clear();
        _lane2X.Clear();
        _lane2Y.Clear();

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

        _averageText.Text = "TRACE 1 selected odd strip · TRACE 2 selected even strip";
        _averagePlot.Plot.Axes.SetLimits(0, BeatNoiseAverager.LaneWindowMs, -0.1, Lane1Baseline + 1.15);
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
        LinePattern pattern = kind == BeatNoiseMarkerKind.A
            ? LinePattern.Dashed
            : LinePattern.Dotted;
        uint color = kind == BeatNoiseMarkerKind.A
            ? _theme.TraceTick
            : _theme.TraceTock;
        VerticalLine marker = GetOrCreateMarker(pool, _mainPlot.Plot, pattern, color);
        marker.X = x;
        marker.IsVisible = true;
    }

    private static BeatSegment? SegmentForSlot(BeatSegmentsSnapshot snapshot, int slot)
    {
        int index = BeatNoiseScopeLogic.SegmentIndexForSlot(slot, snapshot.Segments.Count);
        return index >= 0 ? snapshot.Segments[index] : null;
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
                _lane1MilestoneScatters[i]!.LegendText = milestone.IntervalCount + " avg Trace 1";
            }

            if (_lane2MilestoneScatters[i] != null)
            {
                _lane2MilestoneScatters[i]!.IsVisible = milestone.Lane2.Count > 0;
                _lane2MilestoneScatters[i]!.LegendText = milestone.IntervalCount + " avg Trace 2";
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
                s1.IsVisible = false;
                _lane1MilestoneScatters[i] = s1;
            }

            if (_lane2MilestoneScatters[i] == null)
            {
                Scatter s2 = _averagePlot.Plot.Add.Scatter(_lane2MilestoneX[i], _lane2MilestoneY[i]);
                s2.LineWidth = 1;
                s2.MarkerStyle.IsVisible = false;
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

    private static int? NextAveragePairSelection(int? currentSlot, int clickedSlot, int segmentCount)
    {
        int pairStartSlot = clickedSlot - clickedSlot % 2;
        bool occupied = BeatNoiseScopeLogic.SegmentIndexForSlot(pairStartSlot, segmentCount) >= 0
            || BeatNoiseScopeLogic.SegmentIndexForSlot(pairStartSlot + 1, segmentCount) >= 0;
        if (!occupied)
        {
            return null;
        }

        return currentSlot == pairStartSlot ? null : pairStartSlot;
    }

    private void SetStripAMarker(int slot, BeatSegment? segment, double windowMs)
    {
        VerticalLine? marker = _stripAMarkers[slot];
        if (marker == null) return;

        bool visible = _viewMode == BeatNoiseScopeViewMode.EnvelopeAndStrip
            && segment != null
            && segment.AOffsetMs >= 0.0
            && segment.AOffsetMs <= windowMs
            && windowMs > 0.0;
        marker.IsVisible = visible;
        if (visible)
        {
            marker.X = slot + 0.03 + 0.94 * segment!.AOffsetMs / windowMs;
        }
    }

    private void SetStripCMarker(int slot, BeatSegment? segment, double windowMs)
    {
        VerticalLine? marker = _stripCMarkers[slot];
        if (marker == null)
        {
            return;
        }

        bool visible = _viewMode == BeatNoiseScopeViewMode.EnvelopeAndStrip
            && segment is { CPeakValid: true }
            && segment.CPeakOffsetMs >= 0.0
            && segment.CPeakOffsetMs <= windowMs
            && windowMs > 0.0;
        marker.IsVisible = visible;
        if (visible)
        {
            marker.X = slot + 0.03 + 0.94 * segment!.CPeakOffsetMs / windowMs;
        }
    }

    /// <summary>Review-cursor contract: a dotted marker at the scrub time's in-window offset.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        if (_reviewCursor == null)
        {
            return false;
        }

        BeatSegment? segment = _lastSnapshot is { } snapshot
            ? BeatNoiseScopeLogic.DisplayedSegment(snapshot, _selectedSlot)
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

    private static bool HasUsableCMarker(BeatSegment segment)
    {
        return segment.PeakValue >= MinimumUsablePeakValue
            && segment.CPeakValid
            && segment.CPeakOffsetMs - segment.AOffsetMs >= MinimumValidCSeparationMs;
    }

    private Text AddWeakSignalLabel(Plot plot)
    {
        Text label = plot.Add.Text("WEAK SIGNAL", 0.0, 0.0);
        label.LabelFontSize = 13;
        label.LabelBold = true;
        label.Alignment = Alignment.UpperRight;
        label.IsVisible = false;
        return label;
    }

    private void SetWeakSignalVisible(bool visible)
    {
        if (_weakSignalLabel == null)
        {
            return;
        }

        _weakSignalLabel.IsVisible = visible;
        if (visible)
        {
            _weakSignalLabel.Location = new Coordinates(_rangeMs, _mainYUpper ?? 1.0);
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

        // A = tick green, C = tock red: the same themed event color mapping the
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

        foreach (VerticalLine? marker in _stripAMarkers)
        {
            if (marker != null)
            {
                marker.LineColor = Color.FromARGB(_theme.TraceTick);
            }
        }

        foreach (VerticalLine? marker in _stripCMarkers)
        {
            if (marker != null)
            {
                marker.LineColor = Color.FromARGB(_theme.TraceTock);
            }
        }

        if (_selectionSpan != null)
        {
            _selectionSpan.FillStyle.Color = Color.FromARGB(_theme.TraceTick).WithAlpha(SelectionFillAlpha);
            _selectionSpan.LineStyle.Color = Color.FromARGB(_theme.TraceTick);
        }

        // Lane colors only distinguish the two traces; the labels stay 1/2.
        if (_lane1Scatter != null)
        {
            _lane1Scatter.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_lane2Scatter != null)
        {
            _lane2Scatter.LineColor = Color.FromARGB(_theme.TraceTock);
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
}
