using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private const double RateAutoScalePaddingFraction = 0.08;
    private const double RateLiveMinWindowBeats = 30.0;
    private const double RateLiveMaxWindowBeats = RateScopeRenderer.RatePageWindowBeats;
    internal const double TraceYMinMs = -10.0;
    internal const double TraceYMaxMs = 10.0;
    private const double TraceAnnotationBandFraction = 0.10;
    private const double AnnotationLabelTopPaddingFraction = 0.015;
    private static readonly double[] RateZoomFactors = { 1.0, 4.0, 16.0 };

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
    private bool _rateFollowLive = true;
    private double _rateDataMinX;
    private double _rateDataMaxX;
    private bool _hasRateDataExtent;
    private bool _rateAxisRefreshPending;
    private int _rateZoomIndex;
    private Action<string>? _rateZoomLabelChanged;

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
        _rateAverageAnnotations = new AveragePeriodRateAnnotations(
            textFontFamily,
            labelTopPaddingFraction: AnnotationLabelTopPaddingFraction,
            labelFormatter: FormatAverageSegmentPlotLabel);
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

    public string RateZoomLabel => $"{RateZoomFactors[_rateZoomIndex]:0}x";

    public void SetRateZoomLabelCallback(Action<string> callback)
    {
        _rateZoomLabelChanged = callback;
        callback(RateZoomLabel);
    }

    public void ZoomRateIn() => SetRateZoomIndex(Math.Min(_rateZoomIndex + 1, RateZoomFactors.Length - 1));

    public void ZoomRateOut() => SetRateZoomIndex(Math.Max(_rateZoomIndex - 1, 0));

    public void SetRateZoomFactor(double factor)
    {
        int index = Array.IndexOf(RateZoomFactors, factor);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        SetRateZoomIndex(index);
    }

    private void SetRateZoomIndex(int index)
    {
        if (index == _rateZoomIndex)
        {
            return;
        }

        _rateZoomIndex = index;
        _rateFollowLive = true;
        _rateZoomLabelChanged?.Invoke(RateZoomLabel);
        _hasRateDataExtent = RateDataExtent(out _rateDataMinX, out _rateDataMaxX);
        AutoScaleRateAxesForLiveData();
        if (_hasRateDataExtent)
        {
            UpdateAveragePeriodAnnotations(_lastHistory);
        }
        _tracePlot.Refresh();
    }

    public void CreateGraphs(double rateErrorYScale, int rateDataPoints)
    {
        _rateFollowLive = true;
        _rateZoomIndex = 0;
        _rateZoomLabelChanged?.Invoke(RateZoomLabel);
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
        trace.XLabel("Beats");
        trace.Axes.SetLimitsX(0, RateLiveMinWindowBeats);
        SetFixedTraceYRange();
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
        _tracePlot.Refresh();
    }

    /// <summary>Restores the trace plot: fixed signed-rate Y and the current rate page.</summary>
    public void ResetView()
    {
        _rateFollowLive = true;
        _rateZoomIndex = 0;
        _rateZoomLabelChanged?.Invoke(RateZoomLabel);
        _hasRateDataExtent = RateDataExtent(out _rateDataMinX, out _rateDataMaxX);
        AutoScaleRateAxesForLiveData();
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
                    AutoScaleRateAxesForLiveData();
                }
                else
                {
                    SetFixedTraceYRange();
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
        IReadOnlyList<AveragePeriodRateInterval> intervals =
            history?.AveragePeriodRateIntervals ?? Array.Empty<AveragePeriodRateInterval>();
        _rateAverageAnnotations.Update(_tracePlot.Plot, intervals);
    }

    internal static string FormatAverageSegmentPlotLabel(AveragePeriodRateInterval interval)
    {
        return FormatRate(interval.RateSPerDay) + "  " +
               FormatAmplitude(interval) + "  " +
               FormatBeatError(interval);
    }

    private static string FormatRate(double rateSPerDay)
    {
        string sign = rateSPerDay < 0.0 ? "-" : "+";
        return sign + Math.Abs(rateSPerDay).ToString("F1", CultureInfo.InvariantCulture) + " s/d";
    }

    private static string FormatAmplitude(AveragePeriodRateInterval interval)
    {
        if (!interval.AmplitudeValid)
        {
            return "---°";
        }

        long rounded = (long)Math.Round(interval.AmplitudeDeg, MidpointRounding.AwayFromZero);
        return rounded.ToString(CultureInfo.InvariantCulture) + "°";
    }

    private static string FormatBeatError(AveragePeriodRateInterval interval)
    {
        return interval.BeatErrorValid
            ? interval.BeatErrorMs.ToString("F1", CultureInfo.InvariantCulture) + " ms"
            : "---- ms";
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

    private void AutoScaleRateAxesForLiveData()
    {
        if (!_hasRateDataExtent)
        {
            _tracePlot.Plot.Axes.SetLimitsX(0, RateLiveMinWindowBeats);
            SetFixedTraceYRange();
            return;
        }

        (double left, double right) = RateLiveWindowFor(_rateDataMinX, _rateDataMaxX);
        _tracePlot.Plot.Axes.SetLimitsX(left, right);
        SetFixedTraceYRange();
    }

    private (double Left, double Right) RateLiveWindowFor(double min, double max)
    {
        double dataSpan = Math.Max(max - min, 0.0);
        double paddedSpan = dataSpan * (1.0 + RateAutoScalePaddingFraction * 2.0);
        double zoom = RateZoomFactors[_rateZoomIndex];
        double maxWindow = RateLiveMaxWindowBeats / zoom;
        double minWindow = Math.Min(RateLiveMinWindowBeats, maxWindow);
        double span = Math.Clamp(paddedSpan, minWindow, maxWindow);
        double right = Math.Max(max, span);
        double left = right - span;
        if (left < min && dataSpan <= span)
        {
            left = Math.Max(0.0, min - (span - dataSpan) / 2.0);
            right = left + span;
        }

        return (left, right);
    }

    private void SetFixedTraceYRange()
    {
        (double bottom, double dataTop, double plotTop) = TraceYRangeForZoom();
        _tracePlot.Plot.Axes.SetLimitsY(bottom, plotTop);
        ApplyTraceYTicks(bottom, dataTop);
        _tracePlot.Plot.Axes.Rules.Clear();
        PlotAxisRules.LockYRange(_tracePlot.Plot, bottom, plotTop);
    }

    private (double Bottom, double DataTop, double PlotTop) TraceYRangeForZoom()
    {
        double zoom = RateZoomFactors[_rateZoomIndex];
        double bottom = TraceYMinMs / zoom;
        double dataTop = TraceYMaxMs / zoom;
        double bandHeight = (dataTop - bottom) * TraceAnnotationBandFraction;
        return (bottom, dataTop, dataTop + bandHeight);
    }

    private void ApplyTraceYTicks(double bottom, double dataTop)
    {
        double mid = (bottom + dataTop) * 0.5;
        double lowerMid = (bottom + mid) * 0.5;
        double upperMid = (mid + dataTop) * 0.5;
        var ticks = new ScottPlot.TickGenerators.NumericManual();
        ticks.AddMajor(bottom, FormatAxisTick(bottom));
        ticks.AddMajor(lowerMid, FormatAxisTick(lowerMid));
        ticks.AddMajor(mid, FormatAxisTick(mid));
        ticks.AddMajor(upperMid, FormatAxisTick(upperMid));
        ticks.AddMajor(dataTop, FormatAxisTick(dataTop));
        _tracePlot.Plot.Axes.Left.TickGenerator = ticks;
    }

    private static string FormatAxisTick(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
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
                SetFixedTraceYRange();
                UpdateAveragePeriodAnnotations(_lastHistory);
                _tracePlot.Refresh();
            },
            DispatcherPriority.Background);
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
