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
        _referenceText.Text = ScopeSweepReadout.ReferenceLine(null);

        Plot sweep = _sweepPlot.Plot;
        sweep.Clear();
        _sweepX.Clear();
        _sweepY.Clear();
        ApplyPlotTheme(sweep);
        sweep.YLabel("Amplitude");
        sweep.XLabel("Sweep (ms)");
        _sweepScatter = sweep.Add.Scatter(_sweepX, _sweepY);
        _sweepScatter.LineWidth = 1;
        _sweepScatter.MarkerStyle.IsVisible = false;
        _reviewCursor = AddCursor(sweep);

        ApplySeriesTheme();
        _sweepPlot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        bool dataUpdated = SeriesDataReducer.TryReplaceSeriesData(
            FindSweepSeries(frame), _sweepX, _sweepY, SweepFrameProjector.SweepBinBudget);
        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);

        if (dataUpdated && _followLive)
        {
            _sweepPlot.Plot.Axes.AutoScale();
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
            reviewCursorTimeS, ScopeSweepReadout.WindowMs(_sweepX));
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
        plot.FigureBackground.Color = Color.FromARGB(_theme.SurfaceBg);
        plot.DataBackground.Color = Color.FromARGB(_theme.ScopeBg);
        plot.Axes.Color(Color.FromARGB(_theme.TextPrimary));
        plot.Axes.FrameColor(Color.FromARGB(_theme.ScopeGrid));
        plot.Grid.MajorLineColor = Color.FromARGB(_theme.ScopeGrid);
        plot.Grid.MinorLineColor = Color.FromARGB(_theme.ScopeGrid);
    }

    private static GraphSeriesFrame? FindSweepSeries(AnalysisFrame frame)
    {
        foreach (GraphSeriesFrame series in frame.ScopeSeries)
        {
            if (series.Id == AnalysisGraphSeries.SweepTrace)
            {
                return series;
            }
        }

        return null;
    }
}
