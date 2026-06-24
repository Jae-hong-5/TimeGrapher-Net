using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Trace Display: continuous rate-deviation and amplitude traces over elapsed
/// time, rendered from the cumulative BeatMetricsHistorySnapshot the frame
/// already carries. Each plot shows its acceptable-range band shaded behind the
/// trace with its limit values labelled at the right edge, styled like the
/// Long-Term Performance graph (rate in the bad-deviation color, amplitude in the
/// min/max color) so the two views read consistently. The shaded bands and limit
/// labels alias LongTermAcceptPolicy's normal ranges as a visual reference; the
/// late-running alert banner uses its own asymmetric threshold
/// (TraceAlertEvaluator), so the band marks the normal range, not the alert gate.
/// An alert banner reports late-running and out-of-range amplitude. Re-renders
/// only when the snapshot version changes, so coalesced or repeated frames cost
/// nothing.
/// </summary>
internal sealed class TraceDisplayRenderer
{
    // Accept-band fill alpha and the right-edge inset of the limit-value labels,
    // matching the Long-Term graph so the two displays look identical.
    private const byte AcceptBandFillAlpha = 42;
    // Deviation (±σ) band fill, lighter than the accept band so the two read apart.
    private const byte SigmaBandFillAlpha = 30;
    private const double AcceptLabelXInsetFraction = 0.006;

    // Fixed axis panel sizes (px), the RateScopeRenderer tactic. Locking the left
    // size to one shared value keeps each plot's data area a constant width and
    // keeps the two stacked plots aligned: otherwise ScottPlot sizes each left
    // axis panel to its own Y tick-label text (rate "+10" vs amplitude "300"), so
    // the data areas differ in width and shift whenever the autoscaled range
    // gains/loses a digit. 60 px clears the widest measured label (~56 px) with
    // headroom; the bottom lock holds the data area's vertical extent steady too.
    private const float LeftAxisSizePx = 60.0f;
    // Bottom panel: 42 px on the amplitude pane (shows the shared time axis); a
    // small 10 px reserve on the rate pane (X axis hidden), matching the Long-Term
    // stacked-pane pattern. The amplitude row is enlarged in the tab layout so both
    // data areas stay the same height despite the different bottom reserves.
    private const float BottomAxisSizePx = 42.0f;
    private const float HiddenBottomAxisSizePx = 10.0f;

    private readonly AvaPlot _ratePlot;
    private readonly AvaPlot _amplitudePlot;
    private readonly Border _alertBanner;
    private readonly TextBlock _alertText;

    private readonly List<double> _rateX = new();
    private readonly List<double> _rateY = new();
    private readonly List<double> _amplitudeX = new();
    private readonly List<double> _amplitudeY = new();

    private Scatter? _rateScatter;
    private Scatter? _amplitudeScatter;
    private VerticalSpan? _rateBand;
    private VerticalSpan? _amplitudeBand;
    private Text? _rateMinLabel;
    private Text? _rateMaxLabel;
    private Text? _amplitudeMinLabel;
    private Text? _amplitudeMaxLabel;
    private ReviewCursorLayer? _rateCursor;
    private ReviewCursorLayer? _amplitudeCursor;

    private const float AverageReadoutOffsetXPx = 8.0f;
    private const float AverageReadoutOffsetYPx = 6.0f;
    private HorizontalLine? _rateMeanLine;
    private HorizontalLine? _amplitudeMeanLine;
    private VerticalSpan? _rateSigmaBand;
    private VerticalSpan? _amplitudeSigmaBand;
    private Annotation? _rateAvgLabel;
    private Annotation? _amplitudeAvgLabel;
    private StatsSummary _rateStatsLatest;
    private StatsSummary _amplitudeStatsLatest;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private bool _followLive = true;
    private BeatMetricsHistorySnapshot? _lastHistory;

    // Spline (smooth-curve) rendering of both traces, toggled by the tab's
    // Smoothing button. A view preference, not run data, so it survives a run
    // reset and is re-applied whenever CreateGraphs rebuilds the scatters.
    private bool _smooth = true;

    public TraceDisplayRenderer(
        AvaPlot ratePlot,
        AvaPlot amplitudePlot,
        Border alertBanner,
        TextBlock alertText)
    {
        _ratePlot = ratePlot;
        _amplitudePlot = amplitudePlot;
        _alertBanner = alertBanner;
        _alertText = alertText;

        // A user wheel-zoom or drag-pan drops live-follow and holds the view. The
        // right-edge limit labels are repositioned after the interaction (deferred
        // so ScottPlot has applied the new limits) so they track the view edge even
        // when playback is stopped and no frame re-render is coming.
        _ratePlot.PointerWheelChanged += (_, _) => { _followLive = false; ScheduleAcceptLabelRefresh(); };
        _ratePlot.PointerPressed += (_, _) => _followLive = false;
        _ratePlot.PointerReleased += (_, _) => ScheduleAcceptLabelRefresh();
        _amplitudePlot.PointerWheelChanged += (_, _) => { _followLive = false; ScheduleAcceptLabelRefresh(); };
        _amplitudePlot.PointerPressed += (_, _) => _followLive = false;
        _amplitudePlot.PointerReleased += (_, _) => ScheduleAcceptLabelRefresh();
    }

    // Coalescing gate for the deferred limit-label reposition after a user pan/zoom.
    private bool _acceptRefreshPending;

    private void ScheduleAcceptLabelRefresh()
    {
        if (_acceptRefreshPending)
        {
            return;
        }

        _acceptRefreshPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _acceptRefreshPending = false;
                UpdateAcceptLabels();
                UpdateAverageOverlay();
                _ratePlot.Refresh();
                _amplitudePlot.Refresh();
            },
            DispatcherPriority.Background);
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
        _lastHistory = null;
        _followLive = true;
        _alertBanner.IsVisible = false;

        Plot rate = _ratePlot.Plot;
        rate.Clear();
        _rateX.Clear();
        _rateY.Clear();
        ApplyPlotTheme(rate);
        LockAxisPanels(rate, HiddenBottomAxisSizePx);
        rate.YLabel("Error Rate (s/d)");
        // X axis hidden: the shared elapsed-time axis is shown on the amplitude pane below.
        HideXAxis(rate);
        _rateBand = AddAcceptBand(rate, LongTermAcceptPolicy.Rate);
        _rateSigmaBand = AddSigmaBand(rate);
        _rateScatter = rate.Add.Scatter(_rateX, _rateY);
        _rateScatter.LineWidth = 2;
        _rateScatter.MarkerStyle.IsVisible = false;
        _rateCursor = AddCursor(rate);
        _rateMinLabel = AcceptLabel(rate, LongTermAcceptPolicy.Rate.Min, "+0;-0;0");
        _rateMaxLabel = AcceptLabel(rate, LongTermAcceptPolicy.Rate.Max, "+0;-0;0");
        _rateMeanLine = AddMeanLine(rate);
        _rateAvgLabel = AddAvgLabel(rate);
        PlotAxisRules.ClampLeftEdgeToZero(rate);

        Plot amplitude = _amplitudePlot.Plot;
        amplitude.Clear();
        _amplitudeX.Clear();
        _amplitudeY.Clear();
        ApplyPlotTheme(amplitude);
        LockAxisPanels(amplitude, BottomAxisSizePx);
        amplitude.YLabel("Amplitude(°)");
        amplitude.XLabel("Elapsed (s)");
        _amplitudeBand = AddAcceptBand(amplitude, LongTermAcceptPolicy.Amplitude);
        _amplitudeSigmaBand = AddSigmaBand(amplitude);
        _amplitudeScatter = amplitude.Add.Scatter(_amplitudeX, _amplitudeY);
        _amplitudeScatter.LineWidth = 2;
        _amplitudeScatter.MarkerStyle.IsVisible = false;
        _amplitudeCursor = AddCursor(amplitude);
        _amplitudeMinLabel = AcceptLabel(amplitude, LongTermAcceptPolicy.Amplitude.Min, "0");
        _amplitudeMaxLabel = AcceptLabel(amplitude, LongTermAcceptPolicy.Amplitude.Max, "0");
        _amplitudeMeanLine = AddMeanLine(amplitude);
        _amplitudeAvgLabel = AddAvgLabel(amplitude);
        PlotAxisRules.ClampLeftEdgeToZero(amplitude);

        ApplySmoothing();
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
        PinLiveXAxisToData();
        UpdateAcceptLabels();
        UpdateAverageOverlay();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    /// <summary>
    /// Re-reads the shared accept-band limits: repositions both shaded bands,
    /// rewrites the limit-value labels (their text is baked at creation), and
    /// re-evaluates the alert banner against the last reading, all without clearing
    /// the trace — so an edit shows immediately even while playback is stopped.
    /// </summary>
    public void ApplyAcceptBands()
    {
        (double Min, double Max) rate = LongTermAcceptPolicy.Rate;
        (double Min, double Max) amplitude = LongTermAcceptPolicy.Amplitude;

        if (_rateBand != null)
        {
            _rateBand.Y1 = rate.Min;
            _rateBand.Y2 = rate.Max;
        }

        if (_amplitudeBand != null)
        {
            _amplitudeBand.Y1 = amplitude.Min;
            _amplitudeBand.Y2 = amplitude.Max;
        }

        SetAcceptLabelText(_rateMinLabel, rate.Min, "+0;-0;0");
        SetAcceptLabelText(_rateMaxLabel, rate.Max, "+0;-0;0");
        SetAcceptLabelText(_amplitudeMinLabel, amplitude.Min, "0");
        SetAcceptLabelText(_amplitudeMaxLabel, amplitude.Max, "0");

        // Reframe Y so a widened/moved band (an autoscale participant) stays on
        // screen even while stopped — when running, the next frame does this, so
        // mirror it here. Preserve the user's X window when not following live.
        if (_followLive)
        {
            _ratePlot.Plot.Axes.AutoScale();
            _amplitudePlot.Plot.Axes.AutoScale();
            PinLiveXAxisToData();
        }
        else
        {
            ReframeYPreservingX(_ratePlot);
            ReframeYPreservingX(_amplitudePlot);
        }

        UpdateAcceptLabels();
        UpdateAverageOverlay();
        if (_lastHistory != null)
        {
            UpdateAlerts(_lastHistory);
        }

        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    private static void ReframeYPreservingX(AvaPlot plot)
    {
        AxisLimits before = plot.Plot.Axes.GetLimits();
        plot.Plot.Axes.AutoScale();
        plot.Plot.Axes.SetLimitsX(before.Left, before.Right);
    }

    private static void SetAcceptLabelText(Text? label, double value, string format)
    {
        if (label != null)
        {
            label.LabelText = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Toggles spline (smooth-curve) rendering of the rate and amplitude traces,
    /// driven by the tab's Smoothing button. The flag is retained so a run reset
    /// (which rebuilds the scatters in CreateGraphs) keeps the chosen smoothing.
    /// </summary>
    public void SetSmoothing(bool enabled)
    {
        _smooth = enabled;
        ApplySmoothing();
        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    private void ApplySmoothing()
    {
        if (_rateScatter != null)
        {
            _rateScatter.Smooth = _smooth;
        }

        if (_amplitudeScatter != null)
        {
            _amplitudeScatter.Smooth = _smooth;
        }
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
        _lastHistory = history;

        // History series are already bounded by DecimatingSeries; budget 0 = copy as-is.
        SeriesDataReducer.ReplaceSeriesData(_rateX, _rateY, history.Rate.X, history.Rate.Y, targetPointBudget: 0);
        SeriesDataReducer.ReplaceSeriesData(_amplitudeX, _amplitudeY, history.Amplitude.X, history.Amplitude.Y, targetPointBudget: 0);

        if (_followLive)
        {
            _ratePlot.Plot.Axes.AutoScale();
            _amplitudePlot.Plot.Axes.AutoScale();
            PinLiveXAxisToData();
        }

        _rateStatsLatest = history.RateStats;
        _amplitudeStatsLatest = history.AmplitudeStats;
        UpdateAcceptLabels();
        UpdateAverageOverlay();
        UpdateAlerts(history);

        _ratePlot.Refresh();
        _amplitudePlot.Refresh();
    }

    /// <summary>
    /// Pins both panes' X axis to the plotted data's time extent after a live
    /// autoscale. The accept-range value labels are ScottPlot Text plottables,
    /// which (unlike Scatter or the axis lines) expose no EnableAutoscale opt-out,
    /// so a plain AutoScale folds each visible label's location into the X fit.
    /// Because the labels are repositioned to just inside the margin-padded right
    /// edge every frame, that fed back into the next AutoScale and ratcheted the X
    /// axis steadily past the data, so the trace kept shrinking toward the left.
    /// Re-pinning X to the data extent (the LongTermPerfRenderer tactic) breaks the
    /// loop and locks both stacked panes onto one shared time window. No-op until a
    /// real (non-zero-width) range exists, so AutoScale's default span still frames
    /// the very first point.
    /// </summary>
    private void PinLiveXAxisToData()
    {
        if (!TryGetSharedDataXRange(out double xMin, out double xMax))
        {
            return;
        }

        _ratePlot.Plot.Axes.SetLimitsX(xMin, xMax);
        _amplitudePlot.Plot.Axes.SetLimitsX(xMin, xMax);
    }

    /// <summary>
    /// First/last plotted X across both panes (their series can begin and end a
    /// beat apart). Returns false until a usable, non-zero-width range exists.
    /// </summary>
    private bool TryGetSharedDataXRange(out double xMin, out double xMax)
    {
        xMin = double.MaxValue;
        xMax = double.MinValue;
        if (_rateX.Count > 0)
        {
            if (_rateX[0] < xMin) xMin = _rateX[0];
            if (_rateX[^1] > xMax) xMax = _rateX[^1];
        }

        if (_amplitudeX.Count > 0)
        {
            if (_amplitudeX[0] < xMin) xMin = _amplitudeX[0];
            if (_amplitudeX[^1] > xMax) xMax = _amplitudeX[^1];
        }

        return xMin < xMax;
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

    /// <summary>Review-cursor contract: a vertical marker at the scrub time on both plots.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        bool changed = _rateCursor?.Update(reviewCursorTimeS) ?? false;
        changed |= _amplitudeCursor?.Update(reviewCursorTimeS) ?? false;
        return changed;
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private void ApplySeriesTheme()
    {
        // Per-measure colors aliased from the Long-Term graph: rate in the
        // bad-deviation color, amplitude in the min/max color.
        ThemeMeasure(_rateScatter, _rateBand, _rateMinLabel, _rateMaxLabel, _theme.VarioBad);
        ThemeMeasure(_amplitudeScatter, _amplitudeBand, _amplitudeMinLabel, _amplitudeMaxLabel, _theme.VarioMinMax);

        // Mean line, ±σ band and avg/σ label share the neutral text color so the
        // average overlay reads apart from the colored accept bands (the Long-Term
        // OverallAverage tactic).
        ThemeAverage(_rateMeanLine, _rateSigmaBand, _rateAvgLabel);
        ThemeAverage(_amplitudeMeanLine, _amplitudeSigmaBand, _amplitudeAvgLabel);

        _rateCursor?.ApplyTheme(_theme);
        _amplitudeCursor?.ApplyTheme(_theme);
    }

    private static void ThemeMeasure(Scatter? line, VerticalSpan? band, Text? minLabel, Text? maxLabel, uint argb)
    {
        Color color = Color.FromARGB(argb);
        if (line != null)
        {
            line.LineColor = color;
        }

        if (band != null)
        {
            band.FillStyle.Color = color.WithAlpha(AcceptBandFillAlpha);
            band.LineStyle.Width = 0;
        }

        foreach (Text? label in new[] { minLabel, maxLabel })
        {
            if (label != null)
            {
                label.LabelFontColor = color;
            }
        }
    }

    private void ThemeAverage(HorizontalLine? line, VerticalSpan? band, Annotation? label)
    {
        Color color = Color.FromARGB(_theme.TextPrimary);
        if (line != null)
        {
            line.LineColor = color;
        }

        if (band != null)
        {
            band.FillStyle.Color = color.WithAlpha(SigmaBandFillAlpha);
            band.LineStyle.Width = 0;
        }

        if (label != null)
        {
            label.LabelFontColor = color;
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

    /// <summary>
    /// Pins the left/bottom axis panels to fixed sizes so the data area never
    /// resizes when tick labels change (the RateScopeRenderer tactic). Both plots
    /// share one left size, so their data areas stay aligned as well as constant;
    /// the bottom size is passed per plot (the rate pane hides its X axis and
    /// reserves less than the amplitude pane that carries the shared time axis).
    /// </summary>
    private static void LockAxisPanels(Plot plot, float bottomSize)
    {
        plot.Axes.Left.MinimumSize = LeftAxisSizePx;
        plot.Axes.Left.MaximumSize = LeftAxisSizePx;
        plot.Axes.Bottom.MinimumSize = bottomSize;
        plot.Axes.Bottom.MaximumSize = bottomSize;
    }

    /// <summary>Hides a pane's X axis so the stacked plots share one time axis (shown on the bottom pane).</summary>
    private static void HideXAxis(Plot plot)
    {
        plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        plot.Axes.Bottom.MinorTickStyle.Length = 0;
    }

    /// <summary>
    /// Adds the shaded acceptable-range band behind the trace (the Long-Term graph
    /// style). The band stays in autoscale so the normal range is always visible.
    /// </summary>
    private static VerticalSpan AddAcceptBand(Plot plot, (double Min, double Max) accept)
    {
        VerticalSpan band = plot.Add.VerticalSpan(accept.Min, accept.Max);
        band.LineStyle.Width = 0;
        return band;
    }

    /// <summary>±σ deviation band around the running mean (out of autoscale; hidden until stats exist).</summary>
    private static VerticalSpan AddSigmaBand(Plot plot)
    {
        VerticalSpan band = plot.Add.VerticalSpan(0.0, 0.0);
        band.LineStyle.Width = 0;
        band.EnableAutoscale = false;
        band.IsVisible = false;
        return band;
    }

    /// <summary>Running-average line (dashed, out of autoscale; the Long-Term OverallAverage style).</summary>
    private static HorizontalLine AddMeanLine(Plot plot)
    {
        HorizontalLine line = plot.Add.HorizontalLine(0.0);
        line.LineWidth = 1;
        line.LinePattern = LinePattern.Dashed;
        line.EnableAutoscale = false;
        line.IsVisible = false;
        return line;
    }

    private static Annotation AddAvgLabel(Plot plot)
    {
        Annotation label = plot.Add.Annotation(string.Empty, Alignment.UpperLeft);
        label.LabelBold = true;
        label.LabelFontSize = 14;
        label.OffsetX = AverageReadoutOffsetXPx;
        label.OffsetY = AverageReadoutOffsetYPx;
        label.LabelBackgroundColor = Colors.Transparent;
        label.LabelBorderColor = Colors.Transparent;
        label.LabelShadowColor = Colors.Transparent;
        label.IsVisible = false;
        return label;
    }

    private static Text AcceptLabel(Plot plot, double value, string format)
    {
        Text label = plot.Add.Text(
            value.ToString(format, System.Globalization.CultureInfo.InvariantCulture), 0.0, value);
        label.LabelBold = true;
        label.LabelFontSize = 14;
        label.Alignment = Alignment.MiddleRight;
        label.IsVisible = false;
        return label;
    }

    /// <summary>
    /// Places each plot's limit-value labels at the right edge of the current view,
    /// shown only once a trace exists (before the first beat a right-edge label
    /// would float in an empty plot) and only while the limit is within the Y view.
    /// </summary>
    private void UpdateAcceptLabels()
    {
        PositionAcceptLabels(_ratePlot, _rateX.Count > 0, LongTermAcceptPolicy.Rate, _rateMinLabel, _rateMaxLabel);
        PositionAcceptLabels(_amplitudePlot, _amplitudeX.Count > 0, LongTermAcceptPolicy.Amplitude, _amplitudeMinLabel, _amplitudeMaxLabel);
    }

    private static void PositionAcceptLabels(
        AvaPlot plot, bool hasData, (double Min, double Max) accept, Text? minLabel, Text? maxLabel)
    {
        AxisLimits limits = plot.Plot.Axes.GetLimits();
        double left = limits.Left;
        double right = limits.Right;
        bool usable = hasData && !double.IsNaN(left) && !double.IsNaN(right) && right > left;
        double x = usable ? right - Math.Max(1.0, right - left) * AcceptLabelXInsetFraction : 0.0;

        SetAcceptLabel(minLabel, x, accept.Min,
            usable && accept.Min >= limits.Bottom && accept.Min <= limits.Top, Alignment.LowerRight);
        SetAcceptLabel(maxLabel, x, accept.Max,
            usable && accept.Max >= limits.Bottom && accept.Max <= limits.Top, Alignment.UpperRight);
    }

    private static void SetAcceptLabel(Text? label, double x, double y, bool visible, Alignment alignment)
    {
        if (label == null)
        {
            return;
        }

        label.Location = new Coordinates(x, y);
        label.Alignment = alignment;
        label.IsVisible = visible;
    }

    private void UpdateAverageOverlay()
    {
        PositionAverage(_rateStatsLatest, _rateMeanLine, _rateSigmaBand, _rateAvgLabel, "+0.0;-0.0;0.0", " s/d");
        PositionAverage(_amplitudeStatsLatest, _amplitudeMeanLine, _amplitudeSigmaBand, _amplitudeAvgLabel, "0", "°");
    }

    private static void PositionAverage(
        StatsSummary stats, HorizontalLine? line, VerticalSpan? band, Annotation? label, string valueFormat, string unit)
    {
        bool show = stats.Valid;
        if (line != null)
        {
            line.Y = stats.Mean;
            line.IsVisible = show;
        }

        if (band != null)
        {
            band.Y1 = stats.Mean - stats.Sigma;
            band.Y2 = stats.Mean + stats.Sigma;
            band.IsVisible = show;
        }

        if (label == null)
        {
            return;
        }

        if (!show)
        {
            label.IsVisible = false;
            return;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        label.LabelText = string.Format(
            culture, "avg {0}{1}  σ {2}",
            stats.Mean.ToString(valueFormat, culture), unit, stats.Sigma.ToString("0.0", culture));

        label.IsVisible = true;
    }
}
