using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Long-Term Performance Graph: rate, amplitude and beat error over the whole
/// testing period as three stacked plots, rendered from the cumulative
/// BeatMetricsHistorySnapshot the frame already carries. Each pane shows the
/// bucket-average line, a shaded YMin–YMax band (the range of typical
/// variation) and a dashed overall-average line; the footer reports the
/// overall averages, elapsed time and current resolution. Long durations need
/// no special handling here: the Core DecimatingSeries halves its resolution
/// whenever its fixed capacity fills, so one plotted point inherently spans
/// more seconds — and the display updates less often — as elapsed time grows
/// (the plan's reduced update frequency), while the band keeps the merged
/// buckets' min/max visible. Re-renders only when the snapshot version
/// changes, so coalesced or repeated frames cost nothing. The three panes
/// share one elapsed-time X base, so a user zoom/pan on any pane is linked
/// onto the other two — all three always read on the same time window, while
/// Y stays per-measure (each pane has its own units and range).
/// </summary>
internal sealed class LongTermPerfRenderer
{
    private const byte BandFillAlpha = 44;

    private sealed class Pane
    {
        public required AvaPlot Plot { get; init; }
        public required string YLabel { get; init; }
        public readonly List<double> X = new();
        public readonly List<double> Y = new();
        public readonly List<(double X, double Top, double Bottom)> Band = new();
        public Scatter? BucketLine;
        public FillY? VariationBand;
        public HorizontalLine? OverallAverage;
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

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastVersion;
    private bool _followLive = true;

    // X-axis link: guards the cross-pane SetLimitsX pass against re-entrancy, and
    // coalesces a continuous drag into a single deferred sync (latest source wins).
    private bool _syncing;
    private bool _syncPending;
    private int _syncSource;

    // Number of position turns the markers currently reflect; -1 forces a rebuild
    // (CreateGraphs clears the plots, so the marker lines must be re-added).
    private int _positionMarkerCount = -1;

    public LongTermPerfRenderer(
        AvaPlot ratePlot,
        AvaPlot amplitudePlot,
        AvaPlot beatErrorPlot,
        TextBlock footerText)
    {
        _rate = new Pane { Plot = ratePlot, YLabel = "Rate (s/d)" };
        _amplitude = new Pane { Plot = amplitudePlot, YLabel = "Amplitude (°)" };
        _beatError = new Pane { Plot = beatErrorPlot, YLabel = "Beat error (ms)" };
        _panes = new[] { _rate, _amplitude, _beatError };
        _footerText = footerText;

        for (int i = 0; i < _panes.Length; i++)
        {
            int idx = i;
            AvaPlot plot = _panes[idx].Plot;
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

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        foreach (Pane pane in _panes)
        {
            ApplyPlotTheme(pane.Plot.Plot);
        }

        ApplySeriesTheme();
        RefreshAll();
    }

    public void CreateGraphs()
    {
        _lastVersion = 0;
        _followLive = true;
        _footerText.Text = "";
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

            pane.VariationBand = plot.Add.FillY(pane.Band);
            pane.VariationBand.LineWidth = 0;
            pane.VariationBand.IsVisible = false;
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
        _beatError.Plot.Plot.XLabel("Elapsed (s)");

        ApplySeriesTheme();
        RefreshAll();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    public void ResetView()
    {
        _followLive = true;
        foreach (Pane pane in _panes)
        {
            pane.Plot.Plot.Axes.AutoScale();
        }

        RefreshAll();
    }

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
            // The three panes share one elapsed-time X base, so the X limits
            // transfer directly. Y stays per-pane — each measure has its own
            // units — auto-scaled to the data now in the shared window.
            AxisLimits limits = _panes[source].Plot.Plot.Axes.GetLimits();
            for (int i = 0; i < _panes.Length; i++)
            {
                if (i == source)
                {
                    continue;
                }

                Plot plot = _panes[i].Plot.Plot;
                plot.Axes.SetLimitsX(limits.Left, limits.Right);
                plot.Axes.AutoScaleY();
                _panes[i].Plot.Refresh();
            }
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

        if (history == null || history.Version == _lastVersion)
        {
            if (cursorMoved)
            {
                RefreshAll();
            }

            return;
        }

        _lastVersion = history.Version;

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
            // X is the shared data window; Y stays per-pane (each measure has its
            // own units), auto-scaled to all data in view. A single point (or all
            // points at one time, common in the first frames after a reset) gives
            // a zero-width window, which SetLimitsX cannot represent — fall back
            // to a plain autoscale until the data spans a real range.
            bool sharedWindow = xMax > xMin;
            foreach (Pane pane in _panes)
            {
                if (sharedWindow)
                {
                    pane.Plot.Plot.Axes.AutoScaleY();
                    pane.Plot.Plot.Axes.SetLimitsX(xMin, xMax);
                }
                else
                {
                    pane.Plot.Plot.Axes.AutoScale();
                }
            }
        }

        _footerText.Text = LongTermReadout.Footer(history);
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
        foreach (Pane pane in _panes)
        {
            RenderDetails render = pane.Plot.Plot.RenderManager.LastRender;
            float panel = render.DataRect.Left - render.FigureRect.Left;
            if (panel > maxPanel) maxPanel = panel;
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
        }

        // Refresh directly (not RefreshAll) so this does not recurse.
        if (changed)
        {
            foreach (Pane pane in _panes)
            {
                pane.Plot.Refresh();
            }
        }
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
        // Same measure-to-color mapping the Trace Display uses for rate and
        // amplitude; beat error takes the remaining trace color.
        ThemePane(_rate, _theme.TraceTick);
        ThemePane(_amplitude, _theme.TraceWave);
        ThemePane(_beatError, _theme.TraceTock);
    }

    private void ThemePane(Pane pane, uint argb)
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

        foreach (VerticalLine marker in pane.PositionMarkers)
        {
            StylePositionMarker(marker);
        }

        pane.Cursor?.ApplyTheme(_theme);
    }

    /// <summary>
    /// Reconciles the dashed position-change markers with the snapshot's change
    /// list. Position turns are manual (seconds apart) and the list only grows,
    /// so a rebuild on a changed count is cheap; the start entry (TimeS 0) gives
    /// every run a labelled marker even when the watch is never turned.
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
        marker.LabelOffsetX = 3;
        marker.LabelFontColor = color;
        // Transparent label background so the name never masks the trace behind
        // it (the dashed line plus the bold name stay legible on their own).
        marker.LabelBackgroundColor = Colors.Transparent;
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }
}
