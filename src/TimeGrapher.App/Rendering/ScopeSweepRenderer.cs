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

    // A/C beat markers: dashed vertical lines + text labels per tic/toc phase.
    private VerticalLine? _aTicMarker;
    private Text? _aTicLabel;
    private VerticalLine? _aTocMarker;
    private Text? _aTocLabel;
    private VerticalLine? _cTicMarker;
    private Text? _cTicLabel;
    private VerticalLine? _cTocMarker;
    private Text? _cTocLabel;

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
    private ulong _lastSegmentVersion;
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
        _lastSegmentVersion = 0;
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

        _aTicMarker = AddMarkerLine(sweep, LinePattern.Dashed);
        _aTicLabel  = AddMarkerLabel(sweep);
        _aTocMarker = AddMarkerLine(sweep, LinePattern.Dashed);
        _aTocLabel  = AddMarkerLabel(sweep);
        _cTicMarker = AddMarkerLine(sweep, LinePattern.Dotted);
        _cTicLabel  = AddMarkerLabel(sweep);
        _cTocMarker = AddMarkerLine(sweep, LinePattern.Dotted);
        _cTocLabel  = AddMarkerLabel(sweep);

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

            UpdateSweepMarkerPositions(frame.BeatSegments);
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
    /// the sweep window. X positions are recomputed only when the segment version
    /// changes; Y positions of the text labels track the current signal peak and
    /// are updated on every sweep data refresh.
    /// </summary>
    private void UpdateSweepMarkerPositions(BeatSegmentsSnapshot? snapshot)
    {
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
        double yTop = _sweepY.Count > 0 ? _sweepY.Max() : 1.0;

        ulong sv = snapshot?.Version ?? 0;
        bool segmentChanged = sv != _lastSegmentVersion;
        if (segmentChanged)
        {
            _lastSegmentVersion = sv;

            if (snapshot == null || snapshot.Segments.Count == 0 || windowMs <= 0)
            {
                HideAllMarkers();
                return;
            }

            BeatSegment? latestTic = null, latestToc = null;
            foreach (BeatSegment seg in snapshot.Segments)
            {
                if (seg.IsTic) latestTic = seg;
                else latestToc = seg;
            }

            SetMarkerLine(_aTicMarker, PhaseMs(latestTic, isC: false, windowMs));
            SetMarkerLine(_aTocMarker, PhaseMs(latestToc, isC: false, windowMs));
            SetMarkerLine(_cTicMarker, PhaseMs(
                latestTic is { CPeakValid: true } ? latestTic : null, isC: true, windowMs));
            SetMarkerLine(_cTocMarker, PhaseMs(
                latestToc is { CPeakValid: true } ? latestToc : null, isC: true, windowMs));
        }

        // Always track the signal peak so labels stay near the top of the trace.
        UpdateMarkerLabel(_aTicLabel, _aTicMarker, yTop, "A");
        UpdateMarkerLabel(_aTocLabel, _aTocMarker, yTop, "A");
        UpdateMarkerLabel(_cTicLabel, _cTicMarker, yTop, "C");
        UpdateMarkerLabel(_cTocLabel, _cTocMarker, yTop, "C");
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

    private void HideAllMarkers()
    {
        if (_aTicMarker != null) _aTicMarker.IsVisible = false;
        if (_aTicLabel  != null) _aTicLabel.IsVisible  = false;
        if (_aTocMarker != null) _aTocMarker.IsVisible = false;
        if (_aTocLabel  != null) _aTocLabel.IsVisible  = false;
        if (_cTicMarker != null) _cTicMarker.IsVisible = false;
        if (_cTicLabel  != null) _cTicLabel.IsVisible  = false;
        if (_cTocMarker != null) _cTocMarker.IsVisible = false;
        if (_cTocLabel  != null) _cTocLabel.IsVisible  = false;
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

        Color ticColor = Color.FromARGB(_theme.TraceTick);
        Color tocColor = Color.FromARGB(_theme.TraceTock);
        if (_aTicMarker != null) _aTicMarker.LineColor      = ticColor;
        if (_aTicLabel  != null) _aTicLabel.LabelFontColor  = ticColor;
        if (_aTocMarker != null) _aTocMarker.LineColor      = tocColor;
        if (_aTocLabel  != null) _aTocLabel.LabelFontColor  = tocColor;
        if (_cTicMarker != null) _cTicMarker.LineColor      = ticColor;
        if (_cTicLabel  != null) _cTicLabel.LabelFontColor  = ticColor;
        if (_cTocMarker != null) _cTocMarker.LineColor      = tocColor;
        if (_cTocLabel  != null) _cTocLabel.LabelFontColor  = tocColor;

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

}
