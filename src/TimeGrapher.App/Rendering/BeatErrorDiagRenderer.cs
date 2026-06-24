using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Beat Error Display and Diagnostic Trace: a numeric panel (rate, amplitude,
/// signed beat error, BPH plus the derived DiffTicTac / DiffPeriod / AvgPeriod)
/// above the tic/toc Error Rate traces, with a BeatErrorDiagnostics banner for
/// the separation alert and major-fault slope conditions. The traces refill from
/// the per-frame AnalysisGraphSeries.RateTic/RateToc snapshots every frame
/// already carries (the RateScopeRenderer pattern); the numeric panel and banner
/// re-evaluate only when the cumulative history snapshot version changes.
/// </summary>
internal sealed class BeatErrorDiagRenderer
{
    private const float TraceMarkerSize = 6.0f;
    private const double MinimumBeatWindow = 40.0;
    private const double BeatWindowPadding = 8.0;

    private readonly AvaPlot _tracePlot;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly TextBlock[] _valueTexts;

    private readonly GraphSeriesDefinition[] _rateSeries;
    private readonly List<double>[] _rateX;
    private readonly List<double>[] _rateY;
    private readonly List<Scatter> _ratePlots = new();

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private BeatMetricsHistorySnapshot? _lastHistory;
    private double _rateErrorYScale;
    private int _rateDataPoints;

    public BeatErrorDiagRenderer(AvaPlot tracePlot, Border alertBanner, TextBlock alertText, TextBlock[] valueTexts)
    {
        _tracePlot = tracePlot;
        _alertBanner = alertBanner;
        _alertText = alertText;
        _valueTexts = valueTexts;

        _rateSeries = InfoTabCatalog.Get(InfoTabCatalog.BeatErrorDiagTabId).GraphSeries.ToArray();
        _rateX = new List<double>[_rateSeries.Length];
        _rateY = new List<double>[_rateSeries.Length];
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            _rateX[i] = new List<double>();
            _rateY[i] = new List<double>();
        }
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_tracePlot.Plot);
        ApplySeriesTheme();
        _tracePlot.Refresh();
    }

    /// <summary>
    /// The Beat Error tab draws no shaded band, so only the diagnostic banner
    /// depends on the limit (the separation alert magnitude). Re-evaluate it
    /// against the last reading so an edit shows immediately even while stopped.
    /// </summary>
    public void ApplyAcceptBands()
    {
        if (_lastHistory != null)
        {
            UpdateDiagnosis(_lastHistory);
        }
    }

    public void CreateGraphs(double rateErrorYScale, int rateDataPoints)
    {
        _rateErrorYScale = rateErrorYScale;
        _rateDataPoints = rateDataPoints;
        _lastVersion = 0;
        _lastHistory = null;
        _alertBanner.IsVisible = false;
        foreach (TextBlock value in _valueTexts)
        {
            value.Text = VarioReadout.Missing;
        }

        Plot trace = _tracePlot.Plot;
        trace.Clear();
        ApplyPlotTheme(trace);
        trace.YLabel("Error Rate (ms)");
        trace.XLabel("Beat Index");
        trace.Axes.SetLimitsY(-rateErrorYScale, rateErrorYScale);
        trace.Axes.SetLimitsX(0, Math.Min(rateDataPoints, MinimumBeatWindow));
        trace.Axes.Bottom.TickLabelStyle.IsVisible = true;
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            _rateX[i].Clear();
            _rateY[i].Clear();
        }

        AddTracePlottables();
        trace.ShowLegend();
        PlotAxisRules.ClampLeftEdgeToZero(trace);
        _tracePlot.Refresh();
    }

    /// <summary>Restores the trace plot: signed-rate Y and the current scrolling window.</summary>
    public void ResetView()
    {
        _tracePlot.Plot.Axes.SetLimitsY(-_rateErrorYScale, _rateErrorYScale);
        _tracePlot.Plot.Axes.SetLimitsX(0, _rateDataPoints);
        UpdateAdaptiveXLimits();
        _tracePlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // Review cursor deliberately not rendered here: this trace's x-domain is
        // the WatchMetrics absolute beat index (a scrolling latest-MaxRateDataPoints
        // window), not stream time, so context.ReviewCursorTimeS has no meaningful x
        // mapping on this plot.
        _ = context;

        bool rateUpdated = ReplaceRateSeries(frame);

        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history != null)
        {
            _lastHistory = history;
            if (history.Version != _lastVersion)
            {
                _lastVersion = history.Version;
                UpdateReadout(history);
                UpdateDiagnosis(history);
            }
        }

        if (rateUpdated)
        {
            UpdateAdaptiveXLimits();
            _tracePlot.Refresh();
        }
    }

    private void UpdateReadout(BeatMetricsHistorySnapshot history)
    {
        string[] values = BeatErrorReadout.Values(history);
        for (int i = 0; i < _valueTexts.Length && i < values.Length; i++)
        {
            _valueTexts[i].Text = values[i];
        }
    }

    private void UpdateDiagnosis(BeatMetricsHistorySnapshot history)
    {
        BeatErrorDiagnosis diagnosis = BeatErrorDiagnostics.Evaluate(history);
        _alertBanner.IsVisible = diagnosis.Message != null;
        if (diagnosis.Message != null)
        {
            // The major-fault message is already the stronger wording
            // ("MAJOR FAULT: ..."); the banner styling is shared.
            _alertText.Text = "⚠ " + diagnosis.Message;
        }
    }

    private bool ReplaceRateSeries(AnalysisFrame frame)
    {
        bool updated = false;
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            GraphSeriesFrame? series = SeriesDataReducer.FindSeries(frame.RateSeries, _rateSeries[i].Id);
            if (series == null)
            {
                continue;
            }

            updated |= SeriesDataReducer.TryReplaceSeriesData(series, _rateX[i], _rateY[i], _rateSeries[i].TargetPointBudget);
        }

        return updated;
    }

    private void AddTracePlottables()
    {
        Plot trace = _tracePlot.Plot;
        _ratePlots.Clear();
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            GraphSeriesDefinition spec = _rateSeries[i];
            Scatter sc = trace.Add.Scatter(_rateX[i], _rateY[i]);
            sc.LineWidth = 0;
            sc.MarkerShape = MarkerShape.FilledCircle;
            sc.MarkerSize = TraceMarkerSize;
            sc.MarkerColor = Color.FromARGB(ThemeColor(spec));
            sc.LegendText = spec.Name;
            _ratePlots.Add(sc);
        }
    }

    private void UpdateAdaptiveXLimits()
    {
        double maxBeat = 0.0;
        foreach (List<double> seriesX in _rateX)
        {
            // X is ascending (absolute beat index), so the last point is the newest.
            if (seriesX.Count > 0 && seriesX[^1] > maxBeat)
            {
                maxBeat = seriesX[^1];
            }
        }

        // Grow from a minimum window while the first beats fill in, then scroll: once
        // more than rateDataPoints beats have arrived the window slides to keep the
        // newest rateDataPoints visible and older points leave the left edge.
        double right = Math.Max(MinimumBeatWindow, Math.Ceiling(maxBeat + BeatWindowPadding));
        double left = Math.Max(0.0, right - _rateDataPoints);
        _tracePlot.Plot.Axes.SetLimitsX(left, right);
    }

    private void ApplySeriesTheme()
    {
        for (int i = 0; i < _ratePlots.Count && i < _rateSeries.Length; i++)
        {
            _ratePlots[i].MarkerColor = Color.FromARGB(ThemeColor(_rateSeries[i]));
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);

        plot.Legend.BackgroundColor = Color.FromARGB(_theme.ScopeBg);
        plot.Legend.FontColor = Color.FromARGB(_theme.TextPrimary);
        plot.Legend.OutlineColor = Color.FromARGB(_theme.ScopeGrid);
    }

    // Same tick/tock color mapping the Rate/Scope traces use.
    private uint ThemeColor(GraphSeriesDefinition spec) => spec.Id switch
    {
        AnalysisGraphSeries.RateTic => _theme.TraceTick,
        AnalysisGraphSeries.RateToc => _theme.TraceTock,
        _ => _theme.TraceWave,
    };

}
