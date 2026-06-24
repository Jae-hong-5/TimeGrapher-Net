using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed record LongTermSummaryControls(
    TextBlock Verdict,
    TextBlock Rate,
    TextBlock Amplitude,
    TextBlock BeatError);

/// <summary>
/// Long-Term Performance Graph: rate, amplitude and beat error over the whole
/// testing period as three stacked plots, rendered from the cumulative
/// BeatMetricsHistorySnapshot the frame already carries. Each pane shows the
/// bucket-average line, a shaded YMin–YMax band (the range of typical
/// variation), a dashed overall-average line and a shaded acceptable-range
/// band (from LongTermAcceptPolicy) so the trace can be read against its
/// tolerance at a glance. Long durations need
/// no special handling here: the Core DecimatingSeries halves its resolution
/// whenever its fixed capacity fills, so one plotted point inherently spans
/// more seconds — and the display updates less often — as elapsed time grows
/// (the plan's reduced update frequency), while the band keeps the merged
/// buckets' min/max visible. Re-renders only when the snapshot version
/// changes, so coalesced or repeated frames cost nothing. The three panes
/// share one elapsed-time X base, so a user zoom/pan on any pane is linked
/// onto the other two — all three always read on the same time window, while
/// Y remains auto-scaled per measure (each pane has its own units and range).
/// </summary>
internal sealed class LongTermPerfRenderer
{
    private const byte BandFillAlpha = 44;
    private const byte AcceptBandFillAlpha = 42;
    private const double DefaultLiveWindowS = 24 * 60 * 60;
    private const double PanFraction = 0.25;
    private const double ZoomInFactor = 0.5;
    private const double ZoomOutFactor = 2.0;
    private const double AcceptLabelXInsetFraction = 0.006;
    private const double DataYPadFraction = 0.14;

    // Bottom panel reserved on the upper/middle panes (their X axis is hidden) so
    // the lowest left-axis tick label is not clipped against the pane's edge.
    private const float StackedPaneBottomPadPx = 10f;

    // Fixed left-axis panel size (px), the RateScopeRenderer tactic. Pinning every
    // pane's left panel to one shared value keeps each pane's data area a constant
    // width AND keeps the three stacked panes aligned, so the same elapsed time
    // sits at the same screen x on every pane and the position markers line up.
    // Replaces the former dynamic equalize-to-widest pass, which kept the panes
    // aligned but let the absolute width drift upward as labels grew. 60 px clears
    // the widest measured label (beat error ~56 px) with headroom; the right panel
    // is already a constant 15 px on all panes, so only the left needs pinning.
    private const float LeftAxisSizePx = 60f;

    private sealed class Pane
    {
        public required AvaPlot Plot { get; init; }
        public required string YLabel { get; init; }
        public required string AcceptLabelFormat { get; init; }

        // Acceptable-range corridor for this measure (s/d, °, ms). Drawn as a
        // shaded band so a glance shows whether the long-term trace stays in
        // tolerance; the values come from LongTermAcceptPolicy. Settable so
        // ApplyAcceptBands can re-read the shared limits without clearing history.
        public required (double Min, double Max) Accept { get; set; }

        public readonly List<double> X = new();
        public readonly List<double> Y = new();
        public readonly List<(double X, double Top, double Bottom)> Band = new();
        public VerticalSpan? AcceptBand;
        public Scatter? BucketLine;
        public FillY? VariationBand;
        public HorizontalLine? OverallAverage;
        public Text? AcceptMinLabel;
        public Text? AcceptMaxLabel;
        public ReviewCursorLayer? Cursor;

        // Dashed vertical lines (one per watch-position turn) labelled with the
        // position name; rebuilt only when the change count grows.
        public readonly List<VerticalLine> PositionMarkers = new();
    }

    private readonly Pane _rate;
    private readonly Pane _amplitude;
    private readonly Pane _beatError;
    private readonly Pane[] _panes;
    private readonly LongTermSummaryControls? _summary;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private bool _followLive = true;
    private double? _liveWindowS = DefaultLiveWindowS;
    private BeatMetricsHistorySnapshot? _lastHistory;
    private double? _lastReviewCursorTimeS;
    private Action<double>? _visibleWindowCallback;
    private Action<string>? _reviewMetricsCallback;

    // X-axis link: guards the cross-pane SetLimitsX pass against re-entrancy, and
    // coalesces a continuous drag into a single deferred sync (latest source wins).
    private bool _syncing;
    private bool _syncPending;
    private int _syncSource;

    // Number of position turns the markers currently reflect; -1 forces a rebuild
    // (CreateGraphs clears the plots, so the marker lines must be re-added).
    private int _positionMarkerCount = -1;

    // Optional callback to report the plot area's left/right pixel offsets and
    // data minimum time so the review slider aligns with the graph X-axis.
    private Action<double, double, double>? _sliderAlignmentCallback;

    public LongTermPerfRenderer(
        AvaPlot ratePlot,
        AvaPlot amplitudePlot,
        AvaPlot beatErrorPlot,
        LongTermSummaryControls? summary = null)
    {
        _rate = new Pane { Plot = ratePlot, YLabel = "Error Rate (s/d)", AcceptLabelFormat = "+0;-0;0", Accept = LongTermAcceptPolicy.Rate };
        _amplitude = new Pane { Plot = amplitudePlot, YLabel = "Amplitude(°)", AcceptLabelFormat = "0", Accept = LongTermAcceptPolicy.Amplitude };
        _beatError = new Pane { Plot = beatErrorPlot, YLabel = "Beat Error (ms)", AcceptLabelFormat = "+0.0;-0.0;0.0", Accept = LongTermAcceptPolicy.BeatError };
        _panes = new[] { _rate, _amplitude, _beatError };
        _summary = summary;

        for (int i = 0; i < _panes.Length; i++)
        {
            int idx = i;
            AvaPlot plot = _panes[idx].Plot;
            plot.UserInputProcessor.LeftClickDragPan(true, true, false);
            // Any zoom (wheel), drag (pan / zoom-rectangle, while a button is
            // held), or interaction end re-links the other panes onto this one's
            // X window. PointerPressed just drops live-follow; it changes no axis.
            plot.PointerPressed += (_, _) => _followLive = false;
            plot.PointerWheelChanged += (_, _) => OnUserAxisInteraction(idx);
            plot.PointerReleased += (_, _) => OnUserAxisInteraction(idx);
            plot.PointerMoved += (_, e) =>
            {
                Avalonia.Input.PointerPointProperties props = e.GetCurrentPoint(plot).Properties;
                if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
                {
                    OnUserAxisInteraction(idx);
                }
            };
        }
    }

    /// <summary>
    /// Registers a callback that receives (leftPad, rightPad) pixel offsets of the
    /// plot data area relative to the AvaPlot control bounds. The review slider
    /// uses these to align its track with the graph X-axis.
    /// </summary>
    public void SetSliderAlignmentCallback(Action<double, double, double> callback)
    {
        _sliderAlignmentCallback = callback;
    }

    public void SetVisibleWindowCallback(Action<double> callback)
    {
        _visibleWindowCallback = callback;
    }

    public void SetReviewMetricsCallback(Action<string> callback)
    {
        _reviewMetricsCallback = callback;
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        foreach (Pane pane in _panes)
        {
            ApplyPlotTheme(pane.Plot.Plot);
        }

        ApplySeriesTheme();
        UpdateSummary();
        RefreshAll();
    }

    /// <summary>
    /// Re-reads the shared accept-band limits into all three panes: repositions
    /// each shaded corridor, rewrites the limit-value labels (their text is baked
    /// at creation), and re-frames Y (whose autoscale includes the limits) and the
    /// label/band visibility — keeping the plotted history, so an edit shows
    /// immediately even while playback is stopped.
    /// </summary>
    public void ApplyAcceptBands()
    {
        _rate.Accept = LongTermAcceptPolicy.Rate;
        _amplitude.Accept = LongTermAcceptPolicy.Amplitude;
        _beatError.Accept = LongTermAcceptPolicy.BeatError;

        foreach (Pane pane in _panes)
        {
            if (pane.AcceptBand != null)
            {
                pane.AcceptBand.Y1 = pane.Accept.Min;
                pane.AcceptBand.Y2 = pane.Accept.Max;
            }

            if (pane.AcceptMinLabel != null)
            {
                pane.AcceptMinLabel.LabelText = AcceptLimitLabel(pane.Accept.Min, pane.AcceptLabelFormat);
            }

            if (pane.AcceptMaxLabel != null)
            {
                pane.AcceptMaxLabel.LabelText = AcceptLimitLabel(pane.Accept.Max, pane.AcceptLabelFormat);
            }
        }

        if (TryGetDataXRange(out double xMin, out double xMax))
        {
            if (_followLive)
            {
                ApplyFollowLiveWindow(xMin, xMax);
            }
            else
            {
                foreach (Pane pane in _panes)
                {
                    AxisLimits limits = pane.Plot.Plot.Axes.GetLimits();
                    AutoScaleYIncludingNearbyLimits(pane);
                    UpdateAcceptVisuals(pane, limits.Left, limits.Right);
                }
            }
        }

        UpdateSummary();
        RefreshAll();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _followLive = true;
        _liveWindowS = DefaultLiveWindowS;
        _lastHistory = null;
        _lastReviewCursorTimeS = null;
        SetPlaceholderSummary();
        // plot.Clear() below drops the marker lines; force the next render to re-add.
        _positionMarkerCount = -1;

        foreach (Pane pane in _panes)
        {
            Plot plot = pane.Plot.Plot;
            plot.Clear();
            pane.X.Clear();
            pane.Y.Clear();
            pane.Band.Clear();
            pane.PositionMarkers.Clear();
            ApplyPlotTheme(plot);
            plot.YLabel(pane.YLabel);
            // Pin the left panel to a fixed size so the data area never resizes
            // when the autoscaled Y range gains/loses a digit, and so all three
            // panes share an identical left edge (the RateScopeRenderer tactic).
            plot.Axes.Left.MinimumSize = LeftAxisSizePx;
            plot.Axes.Left.MaximumSize = LeftAxisSizePx;

            pane.AcceptBand = plot.Add.VerticalSpan(pane.Accept.Min, pane.Accept.Max);
            pane.AcceptBand.LineStyle.Width = 0;
            pane.AcceptBand.EnableAutoscale = false;
            pane.VariationBand = plot.Add.FillY(pane.Band);
            pane.VariationBand.LineWidth = 0;
            pane.VariationBand.IsVisible = false;
            plot.Axes.Right.TickLabelStyle.IsVisible = false;
            plot.Axes.Right.MajorTickStyle.Length = 0;
            plot.Axes.Right.MinorTickStyle.Length = 0;

            pane.BucketLine = plot.Add.Scatter(pane.X, pane.Y);
            pane.BucketLine.LineWidth = 2;
            pane.BucketLine.MarkerStyle.IsVisible = false;
            pane.OverallAverage = plot.Add.HorizontalLine(0.0);
            pane.OverallAverage.LineWidth = 1;
            pane.OverallAverage.LinePattern = LinePattern.Dashed;
            pane.OverallAverage.EnableAutoscale = false;
            pane.OverallAverage.IsVisible = false;
            pane.Cursor = AddCursor(plot);
            // Limit-value labels added last so they render above the trace line
            // (matches the Trace display's label Z-order).
            pane.AcceptMinLabel = AcceptLabel(plot, pane.Accept.Min, pane.AcceptLabelFormat);
            pane.AcceptMaxLabel = AcceptLabel(plot, pane.Accept.Max, pane.AcceptLabelFormat);
            PlotAxisRules.ClampLeftEdgeToZero(plot);
        }

        // One shared time axis label on the bottom pane keeps the stack compact.
        // Upper panes hide their X tick labels to avoid redundancy.
        _beatError.Plot.Plot.XLabel("Elapsed (mm:ss)");
        foreach (Pane pane in _panes)
        {
            pane.Plot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                LabelFormatter = ElapsedTickLabel
            };
            if (pane != _beatError)
            {
                pane.Plot.Plot.Axes.Bottom.IsVisible = false;
                pane.Plot.Plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
                pane.Plot.Plot.Axes.Bottom.MajorTickStyle.Length = 0;
                pane.Plot.Plot.Axes.Bottom.MinorTickStyle.Length = 0;
                // A small reserved bottom panel keeps the lowest left-axis tick
                // label from being clipped against the pane edge, while the X tick
                // labels stay hidden (only the bottom pane shows the time axis).
                pane.Plot.Plot.Axes.Bottom.MinimumSize = StackedPaneBottomPadPx;
            }
        }

        ApplySeriesTheme();
        SetInitialXWindow();
        RefreshAll();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    public void ResetView()
    {
        _followLive = true;
        _liveWindowS = DefaultLiveWindowS;
        foreach (Pane pane in _panes)
        {
            pane.Plot.Plot.Axes.AutoScale();
            AutoScaleYIncludingNearbyLimits(pane);
        }

        UpdateSummary();
        RefreshAll();
    }

    public void ShowLive()
    {
        _followLive = true;
        ApplyFollowLiveWindow();
        UpdateSummary();
        RefreshAll();
    }

    public void ShowAll()
    {
        _followLive = true;
        _liveWindowS = null;
        ApplyFollowLiveWindow();
        UpdateSummary();
        RefreshAll();
    }

    public void ShowTimeWindow(double seconds)
    {
        _followLive = true;
        _liveWindowS = seconds;
        ApplyFollowLiveWindow();
        UpdateSummary();
        RefreshAll();
    }

    public void ZoomIn() => ZoomX(ZoomInFactor);

    public void ZoomOut() => ZoomX(ZoomOutFactor);

    public void PanLeft() => PanX(-PanFraction);

    public void PanRight() => PanX(PanFraction);

    /// <summary>
    /// Links a user zoom/pan on one pane onto the others: drops live-follow and
    /// copies this pane's X window to the rest so all three stay on the same
    /// elapsed-time range. Deferred so ScottPlot has applied the new limits to
    /// the source plot before they are read, and coalesced so a continuous drag
    /// queues a single sync (reading the latest limits) instead of one per
    /// PointerMoved.
    /// </summary>
    private void OnUserAxisInteraction(int source)
    {
        _followLive = false;
        _syncSource = source; // latest interaction wins when the pending post runs
        if (_syncPending)
        {
            return;
        }

        _syncPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _syncPending = false;
                SyncXAxisFrom(_syncSource);
            },
            DispatcherPriority.Background);
    }

    private void SyncXAxisFrom(int source)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            AxisLimits limits = _panes[source].Plot.Plot.Axes.GetLimits();
            if (!TryGetDataXRange(out double dataMin, out double dataMax) || dataMax <= dataMin)
            {
                _followLive = false;
                ApplySharedXWindowAndAutoscaleY(limits.Left, limits.Right, refresh: true);
                return;
            }

            (double left, double right) = ClampWindow(limits.Left, limits.Right, dataMin, dataMax);
            double span = Math.Max(1.0, right - left);
            if (IsAtLiveEdge(right, dataMax, span))
            {
                _followLive = true;
                _liveWindowS = span >= dataMax - dataMin ? null : span;
                double followLeft = _liveWindowS is double windowS
                    ? Math.Max(dataMin, dataMax - windowS)
                    : dataMin;
                ApplySharedXWindowAndAutoscaleY(followLeft, dataMax, refresh: true);
                return;
            }

            _followLive = false;
            ApplySharedXWindowAndAutoscaleY(left, right, refresh: true);
        }
        finally
        {
            _syncing = false;
        }
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        BeatMetricsHistorySnapshot? history = frame.MetricsHistory;
        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);
        _lastReviewCursorTimeS = context.ReviewCursorTimeS;

        if (history == null || history.Version == _lastVersion)
        {
            if (history != null)
            {
                _lastHistory = history;
                UpdateSummary();
            }

            if (cursorMoved)
            {
                RefreshAll();
            }

            return;
        }

        _lastVersion = history.Version;
        _lastHistory = history;

        UpdatePane(_rate, history.Rate);
        UpdatePane(_amplitude, history.Amplitude);
        UpdatePane(_beatError, history.BeatError);
        UpdatePositionMarkers(history.PositionChanges);

        // Shared X window taken from the actual plotted data, not each pane's
        // autoscaled limits: early in a run a pane may still be empty (rate needs
        // two beats, amplitude needs a pair), and an empty pane's autoscale would
        // otherwise pollute a union of per-pane limits with a default range and
        // skew every pane's X until all three fill — the start-only misalignment
        // that a Reset View (clean autoscale) hid.
        bool hasData = TryGetDataXRange(out double xMin, out double xMax);

        // Pin every start marker to the shared first point so the starting-
        // position line is identical across the three panes and sits at the left
        // edge, regardless of which series began first.
        if (hasData)
        {
            foreach (Pane pane in _panes)
            {
                if (pane.PositionMarkers.Count > 0)
                {
                    pane.PositionMarkers[0].X = xMin;
                }
            }
        }

        if (_followLive && hasData)
        {
            ApplyFollowLiveWindow(xMin, xMax);
        }
        else if (hasData)
        {
            // Follow-live is off (user-controlled view). Still refresh the accept
            // labels for the current view so they appear once data exists — e.g.
            // when the plot was panned/clicked before the first beat arrived.
            foreach (Pane pane in _panes)
            {
                AxisLimits limits = pane.Plot.Plot.Axes.GetLimits();
                UpdateAcceptVisuals(pane, limits.Left, limits.Right);
            }
        }

        UpdateSummary();
        RefreshAll();
    }

    private void ReportSliderAlignment()
    {
        if (_sliderAlignmentCallback == null)
        {
            return;
        }

        // Use the bottom pane (beat error) — it has the X-axis label and its
        // DataRect represents the visible X span.
        RenderDetails render = _beatError.Plot.Plot.RenderManager.LastRender;
        if (render.DataRect.Width <= 0)
        {
            return;
        }

        double leftPad = render.DataRect.Left - render.FigureRect.Left;
        double rightPad = render.FigureRect.Right - render.DataRect.Right;

        // Report the first data point time so the slider Minimum matches the
        // graph X-axis start (data may not begin at 0 due to accumulation delay).
        double dataMin = 0.0;
        if (TryGetDataXRange(out double xMin, out _))
        {
            dataMin = xMin;
        }

        _sliderAlignmentCallback(leftPad, rightPad, dataMin);
    }

    private static void UpdatePane(Pane pane, MetricsHistorySeries series)
    {
        pane.X.Clear();
        pane.Y.Clear();
        pane.Band.Clear();
        for (int i = 0; i < series.X.Count; i++)
        {
            pane.X.Add(series.X[i]);
            pane.Y.Add(series.Y[i]);
            pane.Band.Add((series.X[i], series.YMax[i], series.YMin[i]));
        }

        // FillY copies its source on SetDataSource; acceptable because the series
        // is capacity-bounded and snapshot versions change at most once per beat
        // (the BeatMetricsHistory publish throttle) of stream time plus once per
        // user state change (position click / sequence reset), which is
        // input-rate bounded.
        if (pane.VariationBand != null)
        {
            pane.VariationBand.SetDataSource(pane.Band);
            pane.VariationBand.IsVisible = pane.Band.Count > 0;
        }

        if (pane.OverallAverage != null)
        {
            double? average = MetricsSeriesMath.Average(series);
            pane.OverallAverage.IsVisible = average.HasValue;
            if (average is double y)
            {
                pane.OverallAverage.Y = y;
            }
        }
    }

    /// <summary>Review-cursor contract: a vertical marker at the scrub time on all three plots.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        bool changed = false;
        foreach (Pane pane in _panes)
        {
            changed |= pane.Cursor?.Update(reviewCursorTimeS) ?? false;
        }

        return changed;
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private void RefreshAll()
    {
        foreach (Pane pane in _panes)
        {
            pane.Plot.Refresh();
        }

        // Run after the refresh so the bottom pane's LastRender reflects the
        // current data area; the panels are fixed-size (LeftAxisSizePx), so no
        // per-pane equalization is needed — only the slider alignment is reported.
        ReportSliderAlignment();
    }

    private void ApplySeriesTheme()
    {
        ThemePane(_rate, _theme.VarioBad, _theme.VarioBad);
        ThemePane(_amplitude, _theme.VarioMinMax, _theme.VarioMinMax);
        ThemePane(_beatError, _theme.TraceTick, _theme.TraceTick);
    }

    private void ThemePane(Pane pane, uint argb, uint acceptArgb)
    {
        Color color = Color.FromARGB(argb);
        if (pane.BucketLine != null)
        {
            pane.BucketLine.LineColor = color;
        }

        if (pane.VariationBand != null)
        {
            pane.VariationBand.FillColor = color.WithAlpha(BandFillAlpha);
        }

        if (pane.OverallAverage != null)
        {
            pane.OverallAverage.LineColor = Color.FromARGB(_theme.TextPrimary);
        }

        // Tolerance corridors use existing per-measure palette colors so the
        // attached 3-lane Long-Term graph remains readable without adding new
        // hard-coded colors.
        Color acceptEdge = Color.FromARGB(acceptArgb);
        if (pane.AcceptBand != null)
        {
            pane.AcceptBand.FillStyle.Color = acceptEdge.WithAlpha(AcceptBandFillAlpha);
            pane.AcceptBand.LineStyle.Color = acceptEdge.WithAlpha(0);
            pane.AcceptBand.LineStyle.Width = 0;
        }

        foreach (Text? label in new[] { pane.AcceptMinLabel, pane.AcceptMaxLabel })
        {
            if (label != null)
            {
                label.LabelFontColor = acceptEdge;
            }
        }

        foreach (VerticalLine marker in pane.PositionMarkers)
        {
            StylePositionMarker(marker);
        }

        pane.Cursor?.ApplyTheme(_theme);
    }

    /// <summary>
    /// Reconciles the dashed position-change markers with the snapshot's change
    /// list. Position turns are manual (seconds apart) and the list only grows,
    /// so a rebuild on a changed count is cheap; the start entry (stamped at the
    /// first plotted point, not 0) gives every run a labelled marker even when
    /// the watch is never turned.
    /// </summary>
    private void UpdatePositionMarkers(IReadOnlyList<PositionChange> changes)
    {
        if (changes.Count == _positionMarkerCount)
        {
            return;
        }

        _positionMarkerCount = changes.Count;
        foreach (Pane pane in _panes)
        {
            Plot plot = pane.Plot.Plot;
            foreach (VerticalLine marker in pane.PositionMarkers)
            {
                plot.Remove(marker);
            }

            pane.PositionMarkers.Clear();

            foreach (PositionChange change in changes)
            {
                VerticalLine marker = plot.Add.VerticalLine(change.TimeS);
                marker.LabelText = change.Position.ShortName();
                StylePositionMarker(marker);
                pane.PositionMarkers.Add(marker);
            }
        }
    }

    /// <summary>
    /// First/last plotted X across every non-empty pane (the panes' series can
    /// begin and end a beat or two apart). Returns false when no pane has data
    /// yet, so callers leave the axes untouched rather than scale to an empty
    /// range.
    /// </summary>
    private bool TryGetDataXRange(out double xMin, out double xMax)
    {
        xMin = double.MaxValue;
        xMax = double.MinValue;
        foreach (Pane pane in _panes)
        {
            if (pane.X.Count > 0)
            {
                if (pane.X[0] < xMin) xMin = pane.X[0];
                if (pane.X[^1] > xMax) xMax = pane.X[^1];
            }
        }

        return xMin <= xMax;
    }

    private void StylePositionMarker(VerticalLine marker)
    {
        // Dashed so it reads distinctly from the dotted review cursor; the
        // markers must never participate in autoscale (the start line sits at the
        // first plotted point and would otherwise drag the data window).
        Color color = Color.FromARGB(_theme.TextPrimary);
        marker.LinePattern = LinePattern.Dashed;
        marker.LineWidth = 1;
        marker.LineColor = color;
        marker.EnableAutoscale = false;

        // Anchor the label at the top edge of the data area, then pull it a few
        // pixels DOWN inside it (negative top padding: ScottPlot draws the
        // opposite-axis label at DataRect.Top - PixelPadding.Top). UpperLeft puts
        // the text's top-left at the anchor, so the name hangs below the top edge
        // and sits to the RIGHT of the dashed line; LabelOffsetX adds a small gap.
        // ManualLabelAlignment is required: RenderLast overwrites LabelAlignment.
        marker.LabelOppositeAxis = true;
        marker.ManualLabelAlignment = Alignment.UpperLeft;
        marker.LabelPixelPadding = new PixelPadding(0, 0, 0, -3);
        marker.LabelOffsetX = 5;
        marker.LabelFontColor = color;
        marker.LabelBold = true;
        marker.LabelBackgroundColor = Colors.Transparent;
    }

    /// <summary>
    /// Formats X-axis tick labels as short elapsed time for early runs and
    /// HH:mm once the Long-Term view reaches hour/day scale.
    /// </summary>
    private static string ElapsedTickLabel(double seconds) => LongTermReadout.FormatElapsedTick(seconds);

    private static string AcceptLimitLabel(double value, string format) =>
        value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private static Text AcceptLabel(Plot plot, double value, string format)
    {
        Text label = plot.Add.Text(AcceptLimitLabel(value, format), 0.0, value);
        label.LabelBold = true;
        label.LabelFontSize = 14;
        label.Alignment = Alignment.MiddleRight;
        label.IsVisible = false;
        return label;
    }

    private void ApplyFollowLiveWindow()
    {
        if (TryGetDataXRange(out double xMin, out double xMax))
        {
            ApplyFollowLiveWindow(xMin, xMax);
        }
    }

    private void ApplyFollowLiveWindow(double xMin, double xMax)
    {
        // A single point gives a zero-width window, which SetLimitsX cannot
        // represent. Plain autoscale keeps the first reading visible until a real
        // time range exists.
        if (xMax <= xMin)
        {
            SetInitialXWindow();
            return;
        }

        double left = _liveWindowS is double windowS
            ? Math.Max(xMin, xMax - windowS)
            : xMin;
        SetSharedXWindow(left, xMax);
    }

    private void SetInitialXWindow() => SetSharedXWindow(0.0, DefaultLiveWindowS);

    private void ZoomX(double factor)
    {
        if (!TryGetDataXRange(out double dataMin, out double dataMax) || dataMax <= dataMin)
        {
            return;
        }

        AxisLimits current = _rate.Plot.Plot.Axes.GetLimits();
        double currentSpan = current.Right > current.Left
            ? current.Right - current.Left
            : dataMax - dataMin;
        double span = currentSpan * factor;
        double center = (current.Left + current.Right) / 2.0;
        if (current.Right <= current.Left)
        {
            center = dataMax;
        }

        SetManualXWindow(center - span / 2.0, center + span / 2.0, dataMin, dataMax);
    }

    private void PanX(double fraction)
    {
        if (!TryGetDataXRange(out double dataMin, out double dataMax) || dataMax <= dataMin)
        {
            return;
        }

        AxisLimits current = _rate.Plot.Plot.Axes.GetLimits();
        double span = current.Right > current.Left
            ? current.Right - current.Left
            : dataMax - dataMin;
        double shift = span * fraction;
        SetManualXWindow(current.Left + shift, current.Right + shift, dataMin, dataMax);
    }

    private void SetManualXWindow(double left, double right, double dataMin, double dataMax)
    {
        (left, right) = ClampWindow(left, right, dataMin, dataMax);
        double span = Math.Max(1.0, right - left);
        _followLive = IsAtLiveEdge(right, dataMax, span);
        _liveWindowS = _followLive && span >= dataMax - dataMin ? null : span;

        if (_followLive)
        {
            double followLeft = _liveWindowS is double windowS
                ? Math.Max(dataMin, dataMax - windowS)
                : dataMin;
            SetSharedXWindow(followLeft, dataMax);
        }
        else
        {
            SetSharedXWindow(left, right);
        }

        UpdateSummary();
        RefreshAll();
    }

    private void SetSharedXWindow(double left, double right)
    {
        ApplySharedXWindowAndAutoscaleY(left, right, refresh: false);
    }

    private void ApplySharedXWindowAndAutoscaleY(double left, double right, bool refresh)
    {
        (double[] tickValues, string[] tickLabels) = LongTermReadout.ElapsedTicks(left, right);
        _visibleWindowCallback?.Invoke(Math.Max(0.0, right - left));
        foreach (Pane pane in _panes)
        {
            pane.Plot.Plot.Axes.Bottom.SetTicks(tickValues, tickLabels);
            pane.Plot.Plot.Axes.SetLimitsX(left, right);
            AutoScaleYIncludingNearbyLimits(pane);
            UpdateAcceptVisuals(pane, left, right);
            if (refresh)
            {
                pane.Plot.Refresh();
            }
        }
    }

    private void AutoScaleYIncludingNearbyLimits(Pane pane)
    {
        Plot plot = pane.Plot.Plot;
        AxisLimits limits = plot.Axes.GetLimits();
        if (!TryGetVisibleYRange(pane, limits.Left, limits.Right, out double dataLo, out double dataHi))
        {
            SetYLimits(plot, pane.Accept.Min, pane.Accept.Max);
            return;
        }

        SetYLimits(
            plot,
            Math.Min(dataLo, pane.Accept.Min),
            Math.Max(dataHi, pane.Accept.Max));
    }

    private static bool TryGetVisibleYRange(
        Pane pane,
        double left,
        double right,
        out double min,
        out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        for (int i = 0; i < pane.X.Count; i++)
        {
            double x = pane.X[i];
            if (x < left || x > right)
            {
                continue;
            }

            double yMin = pane.Band.Count > i ? pane.Band[i].Bottom : pane.Y[i];
            double yMax = pane.Band.Count > i ? pane.Band[i].Top : pane.Y[i];
            if (yMin < min) min = yMin;
            if (yMax > max) max = yMax;
        }

        return min <= max;
    }

    private static void SetYLimits(Plot plot, double min, double max)
    {
        double span = Math.Max(max - min, 1.0);
        double pad = span * DataYPadFraction;
        plot.Axes.SetLimitsY(min - pad, max + pad);
    }

    private static void UpdateAcceptVisuals(Pane pane, double left, double right)
    {
        AxisLimits limits = pane.Plot.Plot.Axes.GetLimits();
        // The accept-range value labels only make sense against a trace: before the
        // first beat the pane shows the placeholder day window, where a right-edge
        // label reads as a stray number floating in an empty plot. The shaded band
        // still shows (it marks the target zone); only the numbers wait for data.
        bool hasData = pane.X.Count > 0;
        bool minVisible = hasData && IsVisibleY(limits, pane.Accept.Min);
        bool maxVisible = hasData && IsVisibleY(limits, pane.Accept.Max);
        bool acceptIntersectsViewport = pane.Accept.Min <= limits.Top && pane.Accept.Max >= limits.Bottom;
        if (pane.AcceptBand != null)
        {
            pane.AcceptBand.IsVisible = acceptIntersectsViewport;
        }

        double x = right - Math.Max(1.0, right - left) * AcceptLabelXInsetFraction;
        SetAcceptLabel(pane.AcceptMinLabel, x, pane.Accept.Min, minVisible, Alignment.LowerRight);
        SetAcceptLabel(pane.AcceptMaxLabel, x, pane.Accept.Max, maxVisible, Alignment.UpperRight);
    }

    private static bool IsVisibleY(AxisLimits limits, double y) =>
        y >= limits.Bottom && y <= limits.Top;

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

    private static (double Left, double Right) ClampWindow(
        double left,
        double right,
        double dataMin,
        double dataMax)
    {
        double dataSpan = dataMax - dataMin;
        double span = Math.Max(1.0, right - left);
        if (span >= dataSpan)
        {
            return (dataMin, dataMax);
        }

        if (left < dataMin)
        {
            right += dataMin - left;
            left = dataMin;
        }

        if (right > dataMax)
        {
            left -= right - dataMax;
            right = dataMax;
        }

        return (Math.Max(dataMin, left), Math.Min(dataMax, right));
    }

    private static bool IsAtLiveEdge(double right, double dataMax, double span)
    {
        double tolerance = Math.Max(1.0, span * 0.001);
        return right >= dataMax - tolerance;
    }

    private void SetPlaceholderSummary()
    {
        if (_summary == null)
        {
            return;
        }

        _summary.Verdict.Text = "COLLECTING";
        _summary.Rate.Text = "Error Rate " + VarioReadout.Missing;
        _summary.Amplitude.Text = "Amplitude " + VarioReadout.Missing;
        _summary.BeatError.Text = "BEAT ERROR " + VarioReadout.Missing;
        ApplySummaryTheme("COLLECTING");
        _reviewMetricsCallback?.Invoke("");
    }

    private void UpdateSummary()
    {
        if (_lastHistory == null)
        {
            SetPlaceholderSummary();
            return;
        }

        BeatMetricsHistorySnapshot history = _lastHistory;
        if (_summary != null)
        {
            string verdict = LongTermReadout.Verdict(history);
            _summary.Verdict.Text = verdict;
            _summary.Rate.Text = LongTermReadout.CurrentRate(history);
            _summary.Amplitude.Text = LongTermReadout.CurrentAmplitude(history);
            _summary.BeatError.Text = LongTermReadout.CurrentBeatError(history);
            ApplySummaryTheme(verdict);
        }

        _reviewMetricsCallback?.Invoke(LongTermReadout.ReviewMetrics(history, _lastReviewCursorTimeS));
    }

    private void ApplySummaryTheme(string verdict)
    {
        if (_summary == null)
        {
            return;
        }

        _summary.Verdict.Foreground = Brush(verdict switch
        {
            "IN TOLERANCE" => _theme.TraceTick,
            "CHECK" => _theme.VarioWarn,
            // Merge: the dedicated VarioPending color was folded into the shared
            // ChromeBorder pending-gray; use it so the verdict tracks that change.
            _ => _theme.ChromeBorder,
        });
        _summary.Rate.Foreground = Brush(_theme.VarioBad);
        _summary.Amplitude.Foreground = Brush(_theme.VarioMinMax);
        _summary.BeatError.Foreground = Brush(_theme.TraceTick);
    }

    private static Avalonia.Media.SolidColorBrush Brush(uint argb) =>
        new(ToAvaloniaColor(argb));

    private static Avalonia.Media.Color ToAvaloniaColor(uint argb) =>
        Avalonia.Media.Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
