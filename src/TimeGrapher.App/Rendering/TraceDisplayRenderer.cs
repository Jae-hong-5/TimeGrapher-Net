using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Trace Display: continuous rate-deviation and amplitude traces for the
/// currently-selected watch position, over that position's re-based elapsed time,
/// rendered from the per-position series the BeatMetricsHistorySnapshot carries
/// (ActivePositionRate/ActivePositionAmplitude). Switching position swaps to that
/// position's accumulation (resuming a previously-measured one, empty until a
/// never-measured one records a beat). The amplitude plot shows the 270-300 degree
/// acceptance band; an alert banner reports late-running and out-of-range
/// amplitude (latest instantaneous reading, inherently the current position); the
/// footer shows since-start and rolling (60 s) averages of the current position.
/// Re-renders only when the snapshot version changes, so coalesced or repeated
/// frames cost nothing.
/// </summary>
internal sealed class TraceDisplayRenderer
{
    private const double RollingWindowS = 60.0;
    private const byte BandFillAlpha = 36;

    // The rate plot always shows at least ±this many s/d: the live auto-fit never
    // zooms in tighter, so a near-on-time watch's small wander is not magnified to
    // fill the pane. It still expands past the floor when the rate genuinely exceeds it.
    private const double RateAxisMinExtentSPerDay = 2.0;

    private readonly AvaPlot _ratePlot;
    private readonly AvaPlot _amplitudePlot;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;
    private readonly TextBlock _summaryText;

    private readonly List<double> _rateX = new();
    private readonly List<double> _rateY = new();
    private readonly List<double> _amplitudeX = new();
    private readonly List<double> _amplitudeY = new();

    private Scatter? _rateScatter;
    private Scatter? _amplitudeScatter;
    private VerticalSpan? _amplitudeBand;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private bool _followLive = true;

    public TraceDisplayRenderer(
        AvaPlot ratePlot,
        AvaPlot amplitudePlot,
        Border alertBanner,
        TextBlock alertText,
        TextBlock summaryText)
    {
        _ratePlot = ratePlot;
        _amplitudePlot = amplitudePlot;
        _alertBanner = alertBanner;
        _alertText = alertText;
        _summaryText = summaryText;

        _ratePlot.PointerWheelChanged += (_, _) => _followLive = false;
        _ratePlot.PointerPressed += (_, _) => _followLive = false;
        _amplitudePlot.PointerWheelChanged += (_, _) => _followLive = false;
        _amplitudePlot.PointerPressed += (_, _) => _followLive = false;
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_ratePlot.Plot);
        ApplyPlotTheme(_amplitudePlot.Plot);
        ApplySeriesTheme();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _followLive = true;
        _alertBanner.IsVisible = false;
        _summaryText.Text = "";

        Plot rate = _ratePlot.Plot;
        rate.Clear();
        _rateX.Clear();
        _rateY.Clear();
        ApplyPlotTheme(rate);
        rate.YLabel("Rate (s/d)");
        rate.XLabel("Elapsed (s)");
        _rateScatter = rate.Add.Scatter(_rateX, _rateY);
        _rateScatter.LineWidth = 2;
        _rateScatter.MarkerStyle.IsVisible = false;
        PlotAxisRules.ClampLeftEdgeToZero(rate);

        Plot amplitude = _amplitudePlot.Plot;
        amplitude.Clear();
        _amplitudeX.Clear();
        _amplitudeY.Clear();
        ApplyPlotTheme(amplitude);
        amplitude.YLabel("Amplitude (°)");
        amplitude.XLabel("Elapsed (s)");
        _amplitudeBand = amplitude.Add.VerticalSpan(
            TraceAlertEvaluator.AmplitudeMinDeg, TraceAlertEvaluator.AmplitudeMaxDeg);
        _amplitudeScatter = amplitude.Add.Scatter(_amplitudeX, _amplitudeY);
        _amplitudeScatter.LineWidth = 2;
        _amplitudeScatter.MarkerStyle.IsVisible = false;
        PlotAxisRules.ClampLeftEdgeToZero(amplitude);

        ApplySeriesTheme();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    public void ResetView()
    {
        _followLive = true;
        AutoScaleRate();
        _amplitudePlot.Plot.Axes.AutoScale();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    // Auto-fit the rate plot, then hold the view open to at least ±RateAxisMinExtentSPerDay.
    private void AutoScaleRate()
    {
        _ratePlot.Plot.Axes.AutoScale();
        PlotAxisRules.EnsureMinimumYRange(_ratePlot.Plot, -RateAxisMinExtentSPerDay, RateAxisMinExtentSPerDay);
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // No review cursor here: pause-and-review scrubbing is a Long-Term-tab-only
        // feature (the scrub cursor clears when leaving that tab), so the Trace tab
        // never receives a scrub time.
        _ = context;
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        if (history == null || history.Version == _lastVersion)
        {
            return;
        }

        _lastVersion = history.Version;

        // Per-position series, already bounded by DecimatingSeries; budget 0 = copy
        // as-is. Trace shows only the currently-selected position's accumulation
        // (re-based elapsed x), so a position switch swaps to that position's data.
        SeriesDataReducer.ReplaceSeriesData(_rateX, _rateY, history.ActivePositionRate.X, history.ActivePositionRate.Y, targetPointBudget: 0);
        SeriesDataReducer.ReplaceSeriesData(_amplitudeX, _amplitudeY, history.ActivePositionAmplitude.X, history.ActivePositionAmplitude.Y, targetPointBudget: 0);

        if (_followLive)
        {
            AutoScaleRate();
            _amplitudePlot.Plot.Axes.AutoScale();
        }

        UpdateAlerts(history);
        UpdateSummaries(history);

        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    private void UpdateAlerts(BeatMetricsHistorySnapshot history)
    {
        TraceAlertState alerts = TraceAlertEvaluator.Evaluate(history);
        _alertBanner.IsVisible = alerts.Message != null;
        if (alerts.Message != null)
        {
            _alertText.Text = "⚠ " + alerts.Message;
        }
    }

    private void UpdateSummaries(BeatMetricsHistorySnapshot history)
    {
        string Format(string label, double? sinceStart, double? rolling, string unit, string numericFormat) =>
            sinceStart is double avg && rolling is double roll
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} avg {1}{3} / last {4:F0}s {2}{3}",
                    label,
                    avg.ToString(numericFormat, System.Globalization.CultureInfo.InvariantCulture),
                    roll.ToString(numericFormat, System.Globalization.CultureInfo.InvariantCulture),
                    unit, RollingWindowS)
                : label + " avg —";

        // Rate is signed; amplitude is an unsigned magnitude shown in whole
        // degrees everywhere else in the app. The averages cover the current
        // position only, matching the per-position graphs above (the rolling
        // window reads the position's re-based elapsed axis without cross-position gaps).
        _summaryText.Text =
            Format("RATE", MetricsSeriesMath.Average(history.ActivePositionRate),
                MetricsSeriesMath.RollingAverage(history.ActivePositionRate, RollingWindowS), " s/d", "+0.0;-0.0;0.0")
            + "   |   "
            + Format("AMP", MetricsSeriesMath.Average(history.ActivePositionAmplitude),
                MetricsSeriesMath.RollingAverage(history.ActivePositionAmplitude, RollingWindowS), "°", "0");
    }

    private void ApplySeriesTheme()
    {
        if (_rateScatter != null)
        {
            _rateScatter.LineColor = Color.FromARGB(_theme.TraceTick);
        }

        if (_amplitudeScatter != null)
        {
            _amplitudeScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        if (_amplitudeBand != null)
        {
            _amplitudeBand.FillStyle.Color = Color.FromARGB(_theme.TraceTick).WithAlpha(BandFillAlpha);
            _amplitudeBand.LineStyle.Color = Color.FromARGB(_theme.TraceTick).WithAlpha((byte)(BandFillAlpha * 2));
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
