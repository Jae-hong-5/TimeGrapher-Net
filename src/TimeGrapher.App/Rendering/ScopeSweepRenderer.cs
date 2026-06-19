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

    private readonly List<double> _sweepX = new();
    private readonly List<double> _sweepY = new();

    private Scatter? _sweepScatter;
    private ReviewCursorLayer? _reviewCursor;

    // Identity gate on the projector's shared-instance pattern: between
    // publish-floor rebuilds (and on every paused-scrub re-route) frames
    // re-attach the same immutable GraphSeriesFrame, and a rebuild always
    // allocates a new one — so reference equality is a correct change
    // detector and skips the redundant copy/autoscale/refresh.
    private GraphSeriesFrame? _lastSweepSeries;
    // Tic-phase offset (ms) published with the last sweep series; used to
    // map the review cursor onto the tic-aligned bin domain.
    private double _ticPhaseOffsetMs;
    // Last known window size (ms); a change re-arms live fitting so the
    // view snaps to [0, new window] when 1x/2x/3x is pressed.
    private double _lastWindowMs;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastReadoutVersion;
    private bool _followLive = true;

    public ScopeSweepRenderer(AvaPlot sweepPlot, TextBlock referenceText)
    {
        _sweepPlot = sweepPlot;
        _referenceText = referenceText;

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

        UpdateReferenceLine(frame.MetricsHistory);

        if (dataUpdated || cursorMoved)
        {
            _sweepPlot.Refresh();
        }
    }

    private void UpdateReferenceLine(BeatMetricsHistorySnapshot? history)
    {
        if (history == null || history.Version == _lastReadoutVersion)
        {
            return;
        }

        _lastReadoutVersion = history.Version;
        _referenceText.Text = ScopeSweepReadout.ReferenceLine(history);
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

    private void ApplySeriesTheme()
    {
        if (_sweepScatter != null)
        {
            _sweepScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

}
