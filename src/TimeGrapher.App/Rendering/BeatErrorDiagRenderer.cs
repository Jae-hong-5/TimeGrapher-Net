using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
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

    private readonly AvaPlot _tracePlot;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly TextBlock[] _valueTexts;

    private readonly GraphSeriesDefinition[] _rateSeries;
    private readonly List<double>[] _rateX;
    private readonly List<double>[] _rateY;
    private readonly List<Scatter> _ratePlots = new();
    private readonly AveragePeriodRateAnnotations _rateAverageAnnotations;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private BeatMetricsHistorySnapshot? _lastHistory;
    private double _rateErrorYScale;
    private bool _rateFollowLive = true;
    private double _rateDataMinX;
    private double _rateDataMaxX;
    private bool _hasRateDataExtent;
    private bool _rateAxisRefreshPending;

    public BeatErrorDiagRenderer(
        AvaPlot tracePlot,
        Border alertBanner,
        TextBlock alertText,
        TextBlock[] valueTexts,
        string textFontFamily)
    {
        _tracePlot = tracePlot;
        _alertBanner = alertBanner;
        _alertText = alertText;
        _valueTexts = valueTexts;

        RateScopeRenderer.LockRatePlotInputToX(_tracePlot);
        RateScopeRenderer.WireLiveFollowPan(
            _tracePlot,
            () => _rateFollowLive = false,
            ScheduleRateAxisRefresh,
            dropOnWheel: false);

        _rateSeries = InfoTabCatalog.Get(InfoTabCatalog.BeatErrorDiagTabId).GraphSeries.ToArray();
        _rateX = new List<double>[_rateSeries.Length];
        _rateY = new List<double>[_rateSeries.Length];
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            _rateX[i] = new List<double>();
            _rateY[i] = new List<double>();
        }
        _rateAverageAnnotations = new AveragePeriodRateAnnotations(textFontFamily);
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_tracePlot.Plot);
        ApplySeriesTheme();
        _rateAverageAnnotations.ApplyTheme(theme);
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
        _rateFollowLive = true;
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
        trace.Axes.SetLimitsX(0, RateScopeRenderer.RatePageWindowBeats);
        trace.Axes.Bottom.TickLabelStyle.IsVisible = true;
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            _rateX[i].Clear();
            _rateY[i].Clear();
        }
        _rateAverageAnnotations.Reset();

        AddTracePlottables();
        trace.ShowLegend();
        _hasRateDataExtent = false;
        trace.Axes.Rules.Clear();
        trace.Axes.Rules.Add(new RateXViewBoundsRule(this, trace.Axes.Bottom));
        PlotAxisRules.LockYRange(trace, -rateErrorYScale, rateErrorYScale);
        _tracePlot.Refresh();
    }

    /// <summary>Restores the trace plot: signed-rate Y and the current 120-beat page.</summary>
    public void ResetView()
    {
        _rateFollowLive = true;
        _tracePlot.Plot.Axes.SetLimitsY(-_rateErrorYScale, _rateErrorYScale);
        _hasRateDataExtent = RateDataExtent(out _rateDataMinX, out _rateDataMaxX);
        _tracePlot.Plot.Axes.SetLimitsX(
            _hasRateDataExtent ? RateScopeRenderer.RatePageWindowFor(_rateDataMaxX).Left : 0,
            _hasRateDataExtent ? RateScopeRenderer.RatePageWindowFor(_rateDataMaxX).Right : RateScopeRenderer.RatePageWindowBeats);
        if (_hasRateDataExtent)
        {
            UpdateAveragePeriodAnnotations(_lastHistory);
        }
        _tracePlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // Review cursor deliberately not rendered here: this trace's x-domain is the
        // WatchMetrics absolute beat index page, not stream time, so context has no
        // meaningful x mapping on this plot.
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
            _hasRateDataExtent = RateDataExtent(out _rateDataMinX, out _rateDataMaxX);
            if (_hasRateDataExtent)
            {
                if (_rateFollowLive)
                {
                    (double left, double right) = RateScopeRenderer.RatePageWindowFor(_rateDataMaxX);
                    _tracePlot.Plot.Axes.SetLimitsX(left, right);
                }

                UpdateAveragePeriodAnnotations(history);
            }

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

    private void UpdateAveragePeriodAnnotations(BeatMetricsHistorySnapshot? history)
    {
        _rateAverageAnnotations.Update(
            _tracePlot.Plot,
            history?.AveragePeriodRateIntervals ?? Array.Empty<AveragePeriodRateInterval>());
    }

    private bool RateDataExtent(out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        bool any = false;
        for (int i = 0; i < _rateX.Length; i++)
        {
            int n = _rateX[i].Count;
            if (n == 0)
            {
                continue;
            }

            if (_rateX[i][0] < min)
            {
                min = _rateX[i][0];
            }

            if (_rateX[i][n - 1] > max)
            {
                max = _rateX[i][n - 1];
            }

            any = true;
        }

        if (!any)
        {
            min = 0;
            max = 0;
            return false;
        }

        return max > min;
    }

    private void ScheduleRateAxisRefresh()
    {
        if (_rateAxisRefreshPending)
        {
            return;
        }

        _rateAxisRefreshPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _rateAxisRefreshPending = false;
                UpdateAveragePeriodAnnotations(_lastHistory);
                _tracePlot.Refresh();
            },
            DispatcherPriority.Background);
    }

    private sealed class RateXViewBoundsRule : IAxisRule
    {
        private readonly BeatErrorDiagRenderer _owner;
        private readonly IXAxis _xAxis;

        public RateXViewBoundsRule(BeatErrorDiagRenderer owner, IXAxis xAxis)
        {
            _owner = owner;
            _xAxis = xAxis;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            if (!_owner._hasRateDataExtent)
            {
                return;
            }

            (double pageLeft, double pageRight) = RateScopeRenderer.RatePageWindowFor(_owner._rateDataMaxX);
            double minLeft = _owner._rateFollowLive ? _owner._rateDataMinX : 0.0;
            double firstPageLeft = _owner._rateFollowLive ? pageLeft : 0.0;
            RateScopeRenderer.ClampViewToPagedExtent(
                _xAxis,
                minLeft,
                pageRight,
                firstPageLeft,
                RateScopeRenderer.RatePageWindowBeats);
        }
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
        PlotThemeHelper.ApplyCompactAxisPanels(plot);

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
