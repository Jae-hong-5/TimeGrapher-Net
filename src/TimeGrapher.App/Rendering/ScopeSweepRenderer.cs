using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Scope Sweep: the Core-folded sweep window envelope (sweep.trace) on a single
/// plot, oscilloscope style — a stable pattern at nominal rate, drift when the
/// watch runs fast or slow. The scatter refills its point lists in place from
/// the frame's replace snapshot; the reference line of current readings under
/// the plot re-renders only when the cumulative history snapshot version
/// changes. The review cursor maps the scrubbed stream time onto its phase
/// within the sweep window.
/// </summary>
internal sealed class ScopeSweepRenderer
{
    private readonly AvaPlot _sweepPlot;
    private readonly TextBlock _referenceText;
    private readonly string _textFontFamily;

    private readonly List<double> _sweepX = new();
    private readonly List<double> _sweepY = new();

    private Scatter? _sweepScatter;
    private ReviewCursorLayer? _reviewCursor;

    // A/C beat markers: one dashed/dotted vertical line + label per tic/toc phase per sweep
    // repetition. 1x shows one set; 2x/3x windows add a second/third copy spaced one beat period apart.
    private const int MaxSweepMultiple = 3;
    private readonly VerticalLine?[] _aTicMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly Text?[]         _aTicLabels  = new Text?[MaxSweepMultiple];
    private readonly VerticalLine?[] _aTocMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly Text?[]         _aTocLabels  = new Text?[MaxSweepMultiple];
    private readonly VerticalLine?[] _cTicMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly Text?[]         _cTicLabels  = new Text?[MaxSweepMultiple];
    private readonly VerticalLine?[] _cTocMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly Text?[]         _cTocLabels  = new Text?[MaxSweepMultiple];

    // Identity gate on the projector's shared-instance pattern: between
    // publish-floor rebuilds (and on every paused-scrub re-route) frames
    // re-attach the same immutable GraphSeriesFrame, and a rebuild always
    // allocates a new one — so reference equality is a correct change
    // detector and skips the redundant copy/autoscale/refresh.
    private GraphSeriesFrame? _lastSweepSeries;
    // Tic-phase offset (ms) published with the last sweep series; used to
    // map the review cursor and beat markers onto the tic-aligned bin domain.
    private double _ticPhaseOffsetMs;
    // Last known window size (ms); a change re-arms live fitting so the
    // view snaps to [0, new window] when 1x/2x/3x is pressed.
    private double _lastWindowMs;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastReadoutVersion;
    private ulong _lastReadoutSegmentVersion;
    private bool _followLive = true;

    public ScopeSweepRenderer(AvaPlot sweepPlot, TextBlock referenceText, string textFontFamily)
    {
        _sweepPlot = sweepPlot;
        _referenceText = referenceText;
        _textFontFamily = textFontFamily;

        _sweepPlot.PointerWheelChanged += (_, _) => _followLive = false;
        _sweepPlot.PointerPressed += (_, _) => _followLive = false;
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_sweepPlot.Plot);
        ApplySeriesTheme();
        _sweepPlot.Refresh();
    }

    public void CreateGraphs()
    {
        _lastReadoutVersion = 0;
        _lastReadoutSegmentVersion = 0;
        _followLive = true;
        _ticPhaseOffsetMs = 0.0;
        _lastWindowMs = 0.0;
        _referenceText.Text = ScopeSweepReadout.ReferenceLine(null);

        Plot sweep = _sweepPlot.Plot;
        sweep.Clear();
        _sweepX.Clear();
        _sweepY.Clear();
        _lastSweepSeries = null;
        ApplyPlotTheme(sweep);
        sweep.YLabel("Signal Level");
        sweep.XLabel("Sweep (ms)");
        _sweepScatter = sweep.Add.Scatter(_sweepX, _sweepY);
        _sweepScatter.LineWidth = 1;
        _sweepScatter.MarkerStyle.IsVisible = false;

        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            _aTicMarkers[k] = AddMarkerLine(sweep, LinePattern.Dashed);
            _aTicLabels[k]  = AddMarkerLabel(sweep);
            _aTocMarkers[k] = AddMarkerLine(sweep, LinePattern.Dashed);
            _aTocLabels[k]  = AddMarkerLabel(sweep);
            _cTicMarkers[k] = AddMarkerLine(sweep, LinePattern.Dotted);
            _cTicLabels[k]  = AddMarkerLabel(sweep);
            _cTocMarkers[k] = AddMarkerLine(sweep, LinePattern.Dotted);
            _cTocLabels[k]  = AddMarkerLabel(sweep);
        }

        _reviewCursor = AddCursor(sweep);
        PlotAxisRules.ClampLeftEdgeToZero(sweep);

        ApplySeriesTheme();
        _sweepPlot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    /// <summary>
    /// Re-arms live auto-fitting after a pan/zoom (the one-way follow-live
    /// latch otherwise sticks until the session restarts — which also hid the
    /// rest of a longer window after a 1x→3x sweep change mid-pan).
    /// </summary>
    public void ResetView()
    {
        _followLive = true;
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
        if (windowMs > 0)
        {
            _sweepPlot.Plot.Axes.AutoScale();
            _sweepPlot.Plot.Axes.SetLimitsX(0, windowMs);
        }
        else
        {
            _sweepPlot.Plot.Axes.AutoScale();
        }
        _sweepPlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        GraphSeriesFrame? sweepSeries = SeriesDataReducer.FindSeries(frame.ScopeSeries, AnalysisGraphSeries.SweepTrace);
        bool dataUpdated = !ReferenceEquals(sweepSeries, _lastSweepSeries) &&
            SeriesDataReducer.TryReplaceSeriesData(sweepSeries, _sweepX, _sweepY, SweepFrameProjector.SweepBinBudget);
        if (dataUpdated)
        {
            _lastSweepSeries = sweepSeries;
            _ticPhaseOffsetMs = sweepSeries!.TicPhaseOffsetMs;

            // Re-arm live fitting when the window size changes (1x/2x/3x pressed)
            // so the view snaps to [0, new window] even if the user had panned.
            double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
            if (Math.Abs(windowMs - _lastWindowMs) > 0.001)
            {
                _lastWindowMs = windowMs;
                _followLive = true;
            }

            UpdateSweepMarkerPositions(frame.BeatSegments, frame.MetricsHistory);
        }

        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);

        if (dataUpdated && _followLive)
        {
            double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
            if (windowMs > 0)
            {
                // Start from the leftmost (tic onset) position; let Y autoscale.
                _sweepPlot.Plot.Axes.AutoScale();
                _sweepPlot.Plot.Axes.SetLimitsX(0, windowMs);
            }
        }

        UpdateReferenceLine(frame.MetricsHistory, frame.BeatSegments);

        if (dataUpdated || cursorMoved)
        {
            _sweepPlot.Refresh();
        }
    }

    private void UpdateReferenceLine(BeatMetricsHistorySnapshot? history, BeatSegmentsSnapshot? segments)
    {
        ulong hv = history?.Version ?? 0;
        ulong sv = segments?.Version ?? 0;
        if (hv == _lastReadoutVersion && sv == _lastReadoutSegmentVersion)
        {
            return;
        }

        _lastReadoutVersion = hv;
        _lastReadoutSegmentVersion = sv;
        _referenceText.Text = ScopeSweepReadout.ReferenceLine(history, segments);
    }

    /// <summary>
    /// Places A (dashed) and C (dotted) vertical markers at each beat phase in
    /// the sweep window. In 1x mode one set of markers is placed; in 2x/3x mode
    /// additional copies are placed at each subsequent beat-period repetition so
    /// the markers remain visible across the full multi-period window.
    /// Both X and Y positions are recomputed on every sweep data refresh so that
    /// a window-size change or phase re-alignment is reflected immediately.
    /// </summary>
    private void UpdateSweepMarkerPositions(BeatSegmentsSnapshot? snapshot,
        BeatMetricsHistorySnapshot? history)
    {
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);

        // Keep last-known positions during a sweep window retune or startup
        // accumulation — hiding here would cause a visible blink every time
        // the 1x/2x/3x selector is pressed.
        if (snapshot == null || snapshot.Segments.Count == 0 || windowMs <= 0)
        {
            return;
        }

        // Beat period: full tic+toc half-cycle. Round the sweep multiple so a
        // 2x window gets exactly 2 copies and a 3x window gets 3.
        double beatPeriodMs = history is { Bph: > 0 }
            ? 3600000.0 / history.Bph
            : windowMs;
        int nReps = Math.Clamp((int)Math.Round(windowMs / beatPeriodMs), 1, MaxSweepMultiple);

        BeatSegment? latestTic = null, latestToc = null;
        foreach (BeatSegment seg in snapshot.Segments)
        {
            if (seg.IsTic) latestTic = seg;
            else latestToc = seg;
        }

        double? aTicPhase = PhaseMs(latestTic, isC: false, windowMs);
        double? aTocPhase = PhaseMs(latestToc, isC: false, windowMs);
        double? cTicPhase = PhaseMs(
            latestTic is { CPeakValid: true } ? latestTic : null, isC: true, windowMs);
        double? cTocPhase = PhaseMs(
            latestToc is { CPeakValid: true } ? latestToc : null, isC: true, windowMs);

        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            bool active = k < nReps;
            SetMarkerLine(_aTicMarkers[k], active ? RepeatPhase(aTicPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_aTocMarkers[k], active ? RepeatPhase(aTocPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_cTicMarkers[k], active ? RepeatPhase(cTicPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_cTocMarkers[k], active ? RepeatPhase(cTocPhase, k, beatPeriodMs, windowMs) : null);
        }

        // Only anchor labels to the signal peak when the sweep has real data;
        // skip the Y update while bins are flat-zero during re-accumulation so
        // labels stay at their last valid height rather than collapsing to zero.
        double yTop = _sweepY.Count > 0 ? _sweepY.Max() : 0.0;
        if (yTop > 0)
        {
            for (int k = 0; k < MaxSweepMultiple; k++)
            {
                UpdateMarkerLabel(_aTicLabels[k], _aTicMarkers[k], yTop, "A");
                UpdateMarkerLabel(_aTocLabels[k], _aTocMarkers[k], yTop, "A");
                UpdateMarkerLabel(_cTicLabels[k], _cTicMarkers[k], yTop, "C");
                UpdateMarkerLabel(_cTocLabels[k], _cTocMarkers[k], yTop, "C");
            }
        }
    }

    /// <summary>
    /// Returns the X position of the k-th repetition of a marker phase within
    /// the sweep window (phase shifted by k beat periods). Returns null when the
    /// base phase is absent or the repeated position falls outside the window.
    /// </summary>
    private static double? RepeatPhase(double? phase, int k, double beatPeriodMs, double windowMs)
    {
        if (phase is not double p) return null;
        double x = p + k * beatPeriodMs;
        return x < windowMs ? x : null;
    }

    private double? PhaseMs(BeatSegment? seg, bool isC, double windowMs)
    {
        if (seg == null) return null;
        double offsetMs = isC ? seg.CPeakOffsetMs : seg.AOffsetMs;
        double phase = (seg.StartTimeS * 1000.0 + offsetMs + _ticPhaseOffsetMs) % windowMs;
        return phase < 0.0 ? phase + windowMs : phase;
    }

    private static void SetMarkerLine(VerticalLine? line, double? x)
    {
        if (line == null) return;
        line.IsVisible = x.HasValue;
        if (x.HasValue) line.X = x.Value;
    }

    private static void UpdateMarkerLabel(Text? label, VerticalLine? line, double yTop, string text)
    {
        if (label == null || line == null) return;
        label.IsVisible = line.IsVisible;
        if (line.IsVisible)
        {
            label.LabelText = text;
            label.Location = new Coordinates(line.X + 1.5, yTop);
        }
    }

/// <summary>Review-cursor contract: a dotted marker at the scrub time's sweep phase.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        if (_reviewCursor == null)
        {
            return false;
        }

        double? phaseMs = ScopeSweepReadout.CursorPhaseMs(
            reviewCursorTimeS, ScopeSweepReadout.WindowMs(_sweepX), _ticPhaseOffsetMs);
        return _reviewCursor.Update(phaseMs);
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private static VerticalLine AddMarkerLine(Plot plot, LinePattern pattern)
    {
        VerticalLine line = plot.Add.VerticalLine(0.0);
        line.LineWidth = 1;
        line.LinePattern = pattern;
        line.IsVisible = false;
        line.EnableAutoscale = false;
        return line;
    }

    private Text AddMarkerLabel(Plot plot)
    {
        Text label = plot.Add.Text("", 0.0, 0.0);
        label.LabelFontName = _textFontFamily;
        label.LabelFontSize = 11;
        label.Alignment = Alignment.UpperLeft;
        label.IsVisible = false;
        return label;
    }

    private void ApplySeriesTheme()
    {
        if (_sweepScatter != null)
        {
            _sweepScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        // A and C escapement markers are colored by event type, not by tic/toc
        // phase: A reuses the green tick color and C the red tock color from the
        // App.axaml palette (no new colors introduced). The tic/toc phase is still
        // read from each marker's position in the sweep window, and A/C also differ
        // by line pattern (A dashed, C dotted).
        Color aColor = Color.FromARGB(_theme.TraceTick); // A -> green
        Color cColor = Color.FromARGB(_theme.TraceTock); // C -> red
        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            if (_aTicMarkers[k] != null) _aTicMarkers[k]!.LineColor     = aColor;
            if (_aTicLabels[k]  != null) _aTicLabels[k]!.LabelFontColor = aColor;
            if (_aTocMarkers[k] != null) _aTocMarkers[k]!.LineColor     = aColor;
            if (_aTocLabels[k]  != null) _aTocLabels[k]!.LabelFontColor = aColor;
            if (_cTicMarkers[k] != null) _cTicMarkers[k]!.LineColor     = cColor;
            if (_cTicLabels[k]  != null) _cTicLabels[k]!.LabelFontColor = cColor;
            if (_cTocMarkers[k] != null) _cTocMarkers[k]!.LineColor     = cColor;
            if (_cTocLabels[k]  != null) _cTocLabels[k]!.LabelFontColor = cColor;
        }

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

}
