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
/// variation), a dashed overall-average line and a dashed acceptable-range
/// corridor (two limit lines per measure, from LongTermAcceptPolicy) so the
/// trace can be read against its tolerance at a glance; the footer reports the
/// overall averages, elapsed time and current resolution. Long durations need
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

    private sealed class Pane
    {
        public required AvaPlot Plot { get; init; }
        public required string YLabel { get; init; }
        public required string AcceptLabelFormat { get; init; }

        // Fixed acceptable-range corridor for this measure (s/d, °, ms). Drawn as
        // two dashed reference lines so a glance shows whether the long-term trace
        // stays in tolerance; the values come from LongTermAcceptPolicy.
        public required (double Min, double Max) Accept { get; init; }

        public readonly List<double> X = new();
        public readonly List<double> Y = new();
        public readonly List<(double X, double Top, double Bottom)> Band = new();
        public VerticalSpan? AcceptBand;
        public Scatter? BucketLine;
        public FillY? VariationBand;
        public HorizontalLine? OverallAverage;
        public HorizontalLine? AcceptMin;
        public HorizontalLine? AcceptMax;
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
    private readonly TextBlock _footerText;
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
        TextBlock footerText,
        LongTermSummaryControls? summary = null)
    {
        _rate = new Pane { Plot = ratePlot, YLabel = "Rate (s/d)", AcceptLabelFormat = "+0;-0;0", Accept = LongTermAcceptPolicy.Rate };
        _amplitude = new Pane { Plot = amplitudePlot, YLabel = "Amplitude (°)", AcceptLabelFormat = "0", Accept = LongTermAcceptPolicy.Amplitude };
        _beatError = new Pane { Plot = beatErrorPlot, YLabel = "Beat Error (ms)", AcceptLabelFormat = "+0.0;-0.0;0.0", Accept = LongTermAcceptPolicy.BeatError };
        _panes = new[] { _rate, _amplitude, _beatError };
        _footerText = footerText;
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
        UpdateSummaryAndFooter();
        RefreshAll();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _followLive = true;
        _liveWindowS = DefaultLiveWindowS;
        _lastHistory = null;
        _lastReviewCursorTimeS = null;
        _footerText.Text = "";
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
            // NOTE: deliberately do NOT reset Axes.Left.MinimumSize here. Once
            // AlignLeftEdges has settled it on the widest natural panel, keeping
            // it across Reset avoids a natural→aligned jump on every Reset press
            // (which read as the left edge toggling); it self-corrects upward if
            // a later run's labels grow wider.

            pane.AcceptBand = plot.Add.VerticalSpan(pane.Accept.Min, pane.Accept.Max);
            pane.AcceptBand.LineStyle.Width = 0;
            pane.AcceptBand.EnableAutoscale = false;
            pane.VariationBand = plot.Add.FillY(pane.Band);
            pane.VariationBand.LineWidth = 0;
            pane.VariationBand.IsVisible = false;
            pane.AcceptMin = plot.Add.HorizontalLine(pane.Accept.Min);
            pane.AcceptMax = plot.Add.HorizontalLine(pane.Accept.Max);
            foreach (HorizontalLine line in new[] { pane.AcceptMin, pane.AcceptMax })
            {
                line.LineWidth = 1.5f;
                line.LinePattern = LinePattern.Dashed;
                line.LabelText = string.Empty;
                line.EnableAutoscale = false;
            }
            plot.Axes.Right.TickLabelStyle.IsVisible = false;
            plot.Axes.Right.MajorTickStyle.Length = 0;
            plot.Axes.Right.MinorTickStyle.Length = 0;
            pane.AcceptMinLabel = AcceptLabel(plot, pane.Accept.Min, pane.AcceptLabelFormat);
            pane.AcceptMaxLabel = AcceptLabel(plot, pane.Accept.Max, pane.AcceptLabelFormat);

            pane.BucketLine = plot.Add.Scatter(pane.X, pane.Y);
            pane.BucketLine.LineWidth = 2;
            pane.BucketLine.MarkerStyle.IsVisible = false;
            pane.OverallAverage = plot.Add.HorizontalLine(0.0);
            pane.OverallAverage.LineWidth = 1;
            pane.OverallAverage.LinePattern = LinePattern.Dashed;
            pane.OverallAverage.EnableAutoscale = false;
            pane.OverallAverage.IsVisible = false;
            pane.Cursor = AddCursor(plot);
            PlotAxisRules.ClampLeftEdgeToZero(plot);
        }

        // One shared time axis label on the bottom pane keeps the stack compact.
        // Upper panes hide their X tick labels to avoid redundancy.
        _beatError.Plot.Plot.XLabel("Elapsed");
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

        UpdateSummaryAndFooter();
        RefreshAll();
    }

    public void ShowLive()
    {
        _followLive = true;
        ApplyFollowLiveWindow();
        UpdateSummaryAndFooter();
        RefreshAll();
    }

    public void ShowAll()
    {
        _followLive = true;
        _liveWindowS = null;
        ApplyFollowLiveWindow();
        UpdateSummaryAndFooter();
        RefreshAll();
    }

    public void ShowTimeWindow(double seconds)
    {
        _followLive = true;
        _liveWindowS = seconds;
        ApplyFollowLiveWindow();
        UpdateSummaryAndFooter();
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
                UpdateSummaryAndFooter();
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

        UpdateSummaryAndFooter();
        RefreshAll();
    }

    /// <summary>
    /// Equalizes the three panes' left data-area edge so the same elapsed time
    /// sits at the same screen x on every pane. Each pane's Y tick labels have a
    /// different width (rate "0" vs amplitude "304.5" vs beat error "0.02"),
    /// which otherwise offsets the plot area and makes the stacked graphs — and
    /// the position markers — look misaligned. Reads the actual rendered pixels,
    /// so it is DPI-correct.
    ///
    /// The target is the widest pane's left-axis PANEL width (DataRect.Left minus
    /// the figure's left margin), not DataRect.Left itself: DataRect.Left includes
    /// the margin, and feeding that back into MinimumSize (a panel size) overshot
    /// by the margin every render, which read as the left edge toggling on each
    /// Reset. With the panel width the fixed point is exact — MinimumSize settles
    /// on the widest natural panel and the per-pane guard then leaves it alone.
    /// </summary>
    private void AlignLeftEdges()
    {
        float maxPanel = 0f;
        float maxRightPanel = 0f;
        foreach (Pane pane in _panes)
        {
            RenderDetails render = pane.Plot.Plot.RenderManager.LastRender;
            float panel = render.DataRect.Left - render.FigureRect.Left;
            float rightPanel = render.FigureRect.Right - render.DataRect.Right;
            if (panel > maxPanel) maxPanel = panel;
            if (rightPanel > maxRightPanel) maxRightPanel = rightPanel;
        }

        if (maxPanel <= 0f)
        {
            return; // nothing rendered yet
        }

        // Only widen panes that are below the target; the guard makes this a fixed
        // point (a pane already at the target is skipped), so there is no feedback.
        bool changed = false;
        foreach (Pane pane in _panes)
        {
            if (maxPanel - pane.Plot.Plot.Axes.Left.MinimumSize > 0.5f)
            {
                pane.Plot.Plot.Axes.Left.MinimumSize = maxPanel;
                changed = true;
            }

            if (maxRightPanel > 0f && maxRightPanel - pane.Plot.Plot.Axes.Right.MinimumSize > 0.5f)
            {
                pane.Plot.Plot.Axes.Right.MinimumSize = maxRightPanel;
                changed = true;
            }
        }

        // Refresh directly (not RefreshAll) so this does not recurse.
        if (changed)
        {
            foreach (Pane pane in _panes)
            {
                pane.Plot.Refresh();
            }
        }

        // Report the data area offsets so the review slider can align with the X-axis.
        ReportSliderAlignment();
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
        // is capacity-bounded and snapshot versions change at most once per
        // BeatMetricsHistory.SnapshotMinIntervalS of stream time plus once per
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

        // Run after the refresh so each pane's LastRender reflects its current Y
        // label widths — including the empty post-Reset state, where the panes
        // would otherwise keep mismatched left edges until the next data frame.
        AlignLeftEdges();
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

        if (pane.AcceptMin != null)
        {
            pane.AcceptMin.LineColor = acceptEdge;
        }

        if (pane.AcceptMax != null)
        {
            pane.AcceptMax.LineColor = acceptEdge;
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
        label.LabelFontSize = 12;
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

        UpdateSummaryAndFooter();
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
        bool minVisible = IsVisibleY(limits, pane.Accept.Min);
        bool maxVisible = IsVisibleY(limits, pane.Accept.Max);
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
        _summary.Rate.Text = "RATE " + VarioReadout.Missing;
        _summary.Amplitude.Text = "AMPLITUDE " + VarioReadout.Missing;
        _summary.BeatError.Text = "BEAT ERROR " + VarioReadout.Missing;
        ApplySummaryTheme("COLLECTING");
        _reviewMetricsCallback?.Invoke("");
    }

    private void UpdateSummaryAndFooter()
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

        _footerText.Text = LongTermReadout.Footer(history, _lastReviewCursorTimeS);
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
        plot.FigureBackground.Color = Color.FromARGB(_theme.ScopeBg);
    }
}
