using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Trace Display: continuous rate-deviation and amplitude traces over elapsed
/// time, rendered from the cumulative BeatMetricsHistorySnapshot the frame
/// already carries. The amplitude plot shows the 270-300 degree acceptance band;
/// an alert banner reports late-running and out-of-range amplitude; the footer
/// shows since-start and rolling (60 s) averages. Re-renders only when the
/// snapshot version changes, so coalesced or repeated frames cost nothing.
/// </summary>
internal sealed class TraceDisplayRenderer
{
    private const double RollingWindowS = 60.0;
    private const byte BandFillAlpha = 36;

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
    private LinePlot? _rateCursor;
    private LinePlot? _amplitudeCursor;

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
        _rateCursor = AddCursor(rate);

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
        _amplitudeCursor = AddCursor(amplitude);

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
        _ratePlot.Plot.Axes.AutoScale();
        _amplitudePlot.Plot.Axes.AutoScale();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);

        if (history == null || history.Version == _lastVersion)
        {
            if (cursorMoved)
            {
                _ratePlot.Refresh();
                _amplitudePlot.Refresh();
            }

            return;
        }

        _lastVersion = history.Version;

        ReplaceSeries(history.Rate, _rateX, _rateY);
        ReplaceSeries(history.Amplitude, _amplitudeX, _amplitudeY);

        if (_followLive)
        {
            _ratePlot.Plot.Axes.AutoScale();
            _amplitudePlot.Plot.Axes.AutoScale();
        }

        UpdateAlerts(history);
        UpdateSummaries(history);

        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    private static void ReplaceSeries(MetricsHistorySeries source, List<double> x, List<double> y)
    {
        x.Clear();
        y.Clear();
        for (int i = 0; i < source.X.Count; i++)
        {
            x.Add(source.X[i]);
            y.Add(source.Y[i]);
        }
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
        string Format(string label, double? sinceStart, double? rolling, string unit) =>
            sinceStart is double avg && rolling is double roll
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} avg {1:+0.0;-0.0;0.0}{3} / last {4:F0}s {2:+0.0;-0.0;0.0}{3}",
                    label, avg, roll, unit, RollingWindowS)
                : label + " avg —";

        _summaryText.Text =
            Format("RATE", MetricsSeriesMath.Average(history.Rate),
                MetricsSeriesMath.RollingAverage(history.Rate, RollingWindowS), " s/d")
            + "   |   "
            + Format("AMP", MetricsSeriesMath.Average(history.Amplitude),
                MetricsSeriesMath.RollingAverage(history.Amplitude, RollingWindowS), "°");
    }

    /// <summary>Review-cursor contract: a vertical marker at the scrub time on both plots.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        bool visible = reviewCursorTimeS is not null;
        bool changed = false;

        foreach (LinePlot? cursor in new[] { _rateCursor, _amplitudeCursor })
        {
            if (cursor == null)
            {
                continue;
            }

            if (cursor.IsVisible != visible)
            {
                cursor.IsVisible = visible;
                changed = true;
            }

            if (reviewCursorTimeS is double t && Math.Abs(cursor.Start.X - t) > double.Epsilon)
            {
                cursor.Line = new CoordinateLine(t, -1e6, t, 1e6);
                changed = true;
            }
        }

        return changed;
    }

    private LinePlot AddCursor(Plot plot)
    {
        LinePlot cursor = plot.Add.Line(0.0, 0.0, 0.0, 0.0);
        cursor.MarkerStyle.IsVisible = false;
        cursor.LineWidth = 1;
        cursor.LinePattern = LinePattern.Dotted;
        cursor.IsVisible = false;
        return cursor;
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

        foreach (LinePlot? cursor in new[] { _rateCursor, _amplitudeCursor })
        {
            if (cursor != null)
            {
                cursor.LineColor = Color.FromARGB(_theme.TextPrimary);
            }
        }
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
}
