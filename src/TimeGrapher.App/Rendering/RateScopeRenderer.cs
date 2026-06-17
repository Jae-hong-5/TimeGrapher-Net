using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class RateScopeRenderer
{
    private readonly AvaPlot _scopePlot;
    private readonly AvaPlot _ratePlot;
    private readonly string _textFontFamily;

    private readonly GraphSeriesDefinition[] _scopeSeries;
    private readonly GraphSeriesDefinition[] _rateSeries;

    private readonly List<double>[] _scopeX;
    private readonly List<double>[] _scopeY;
    private readonly List<double>[] _rateX;
    private readonly List<double>[] _rateY;

    // Scope event markers are pooled: a render tick repositions the existing
    // LinePlot/Text plottables in place and hides the surplus instead of
    // removing and re-allocating the whole 2 s marker window (~100+ plottables)
    // at up to 30 Hz on the UI thread. Hidden plottables are excluded from
    // ScottPlot's autoscale, so leftovers cannot distort the Y fit.
    private readonly List<LinePlot> _scopeLinePool = new();
    private readonly List<Text> _scopeTextPool = new();
    // Source (theme-independent) marker colors per pool slot: after a stop no
    // frame re-render refreshes the pool, so a theme toggle must re-map these
    // through ThemeColor itself.
    private readonly List<uint> _scopeLineSourceColors = new();
    private readonly List<uint> _scopeTextSourceColors = new();
    private int _scopeLinesUsed;
    private int _scopeTextsUsed;
    private readonly List<Scatter> _scopePlots = new();
    private readonly List<Scatter> _ratePlots = new();
    private ReviewCursorLayer? _scopeReviewCursor;
    private PlotThemePalette _theme = PlotThemePalette.Current;

    // The scope auto-follows incoming audio (scrolls its X window each frame). Once the
    // user pans/zooms it, we stop following so the view stays put; ResetView() re-enables it.
    private bool _scopeFollowLive = true;
    private double _rateErrorYScale;
    private int _rateDataPoints;
    private int _sampleRate = 44100;

    // Default live scope window shown on screen (500 ms) and the maximum span the
    // user may zoom out to (2 s). The Core retains 2 s of scope history
    // (ScopeRateFrameProjector.ScopeSnapshotSeconds), so 2 s is also the hard
    // zoom-out ceiling enforced by ScopeXViewBoundsRule.
    private const double DefaultScopeWindowSeconds = 0.5;
    private const double MaxScopeWindowSeconds = 2.0;

    // Fixed scope axis panel sizes (px). Locking the bottom/left axis sizes keeps
    // the data area constant: otherwise ScottPlot resizes the plot when the tick
    // labels change (e.g. a time label scrolls out of view, or the Y autoscale
    // gains/loses a digit), which makes the whole graph grow/shrink.
    private const float ScopeLeftAxisSizePx = 52.0f;
    private const float ScopeBottomAxisSizePx = 42.0f;

    // Live drawn-data extent on the scope X base (oldest..newest retained tick),
    // refreshed each frame. ScopeXViewBoundsRule confines the X view to it so a
    // pan/zoom stops at the data edges — no empty margin, start point pinned to
    // the data — while still allowing the user to move within the retained window.
    private double _dataMinX;
    private double _dataMaxX;
    private bool _hasDataExtent;

    // Coalescing gate for the deferred tick refresh after a user pan/zoom: a drag
    // fires PointerMoved many times per second, but only one deferred recompute
    // needs to be queued (it reads the latest limits when it runs).
    private bool _scopeAxisRefreshPending;
    public RateScopeRenderer(AvaPlot scopePlot, AvaPlot ratePlot, string textFontFamily)
    {
        _scopePlot = scopePlot;
        _ratePlot = ratePlot;
        _textFontFamily = textFontFamily;

        // Keep the default ScottPlot interactions: drag to swipe/pan and wheel zoom
        // (including the Shift/Ctrl modifiers). Any user pan/zoom drops live-follow
        // so the view holds where the user left it; the ScopeXViewBoundsRule then
        // clamps that view to the retained data extent, so a pan/zoom can never drag
        // the X-axis start point past the data ("move, but the start stays pinned to
        // the data") — the Multi-Filter Scope behavior.
        // Any user pan/zoom drops live-follow so the view holds where the user left
        // it; the ScopeXViewBoundsRule then clamps that view to the retained data
        // extent. After the interaction we re-lay the fixed 0.2 s time ruler over
        // the new X range — ScottPlot's NumericManual is a fixed tick set, so it
        // must be regenerated when the user zoom/pan changes the range (otherwise
        // the newly revealed span shows no time labels on zoom-out).
        _scopePlot.PointerWheelChanged += (_, _) =>
        {
            _scopeFollowLive = false;
            ScheduleScopeAxisRefresh();
        };
        _scopePlot.PointerPressed += (_, _) => _scopeFollowLive = false;
        _scopePlot.PointerReleased += (_, _) => ScheduleScopeAxisRefresh();
        _scopePlot.PointerMoved += (_, e) =>
        {
            if (e.GetCurrentPoint(_scopePlot).Properties.IsLeftButtonPressed)
            {
                ScheduleScopeAxisRefresh();
            }
        };

        GraphSeriesDefinition[] graphSeries = InfoTabCatalog.RateScope.GraphSeries.ToArray();
        _scopeSeries = graphSeries.Where(series => series.RenderMode == GraphSeriesRenderMode.Line).ToArray();
        _rateSeries = graphSeries.Where(series => series.RenderMode == GraphSeriesRenderMode.Points).ToArray();

        _scopeX = CreateSeriesLists(_scopeSeries.Length);
        _scopeY = CreateSeriesLists(_scopeSeries.Length);
        _rateX = CreateSeriesLists(_rateSeries.Length);
        _rateY = CreateSeriesLists(_rateSeries.Length);
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_scopePlot.Plot);
        ApplyPlotTheme(_ratePlot.Plot);
        ApplySeriesTheme();
        _scopePlot.Refresh();
        _ratePlot.Refresh();
    }

    public void CreateGraphs(double rateErrorYScale, int rateDataPoints)
    {
        _rateErrorYScale = rateErrorYScale;
        _rateDataPoints = rateDataPoints;
        _scopeFollowLive = true;
        Plot scope = _scopePlot.Plot;
        scope.Clear();
        ApplyPlotTheme(scope);
        scope.YLabel("Signal level");
        scope.XLabel("Time (s)");
        scope.Axes.SetLimitsY(0, 0.1);
        // Fixed-interval time ruler (matches the Multi-Filter Scope): a minor tick
        // every 0.1 s and a labeled major tick every 0.2 s. The exact tick positions
        // are (re)generated per window in ApplyScopeTimeTicks; here we only fix the
        // tick mark styling.
        scope.Axes.Bottom.TickLabelStyle.IsVisible = true;
        scope.Axes.Bottom.MinorTickStyle.Length = 3;
        scope.Axes.Bottom.MajorTickStyle.Length = 6;
        // Lock the axis panel sizes so the data area never resizes when tick labels
        // appear/disappear (scrolling time labels, Y autoscale digit changes).
        scope.Axes.Left.MinimumSize = ScopeLeftAxisSizePx;
        scope.Axes.Left.MaximumSize = ScopeLeftAxisSizePx;
        scope.Axes.Bottom.MinimumSize = ScopeBottomAxisSizePx;
        scope.Axes.Bottom.MaximumSize = ScopeBottomAxisSizePx;
        ClearSeriesData(_scopeX, _scopeY);
        DropScopeMarkerPool();
        AddScopePlottables();
        _scopeReviewCursor = AddReviewCursor(scope);
        scope.ShowLegend();
        _hasDataExtent = false;
        scope.Axes.Rules.Clear();
        scope.Axes.Rules.Add(new ScopeXViewBoundsRule(this, scope.Axes.Bottom));

        Plot rate = _ratePlot.Plot;
        rate.Clear();
        ApplyPlotTheme(rate);
        rate.YLabel("Error Rate(ms)");
        rate.XLabel("Beat Index");
        rate.Axes.SetLimitsY(-rateErrorYScale, rateErrorYScale);
        rate.Axes.SetLimitsX(0, rateDataPoints);
        ClearSeriesData(_rateX, _rateY);
        AddRatePlottables();
        rate.ShowLegend();
        PlotAxisRules.ClampLeftEdgeToZero(rate);

        _scopePlot.Refresh();
        _ratePlot.Refresh();
    }

    public void Reset(double rateErrorYScale, int rateDataPoints)
    {
        _rateErrorYScale = rateErrorYScale;
        _rateDataPoints = rateDataPoints;
        _scopeFollowLive = true;
        Plot scope = _scopePlot.Plot;
        scope.Clear();
        ApplyPlotTheme(scope);
        ClearSeriesData(_scopeX, _scopeY);
        DropScopeMarkerPool();
        AddScopePlottables();
        _scopeReviewCursor = AddReviewCursor(scope);
        _hasDataExtent = false;
        scope.Axes.Rules.Clear();
        scope.Axes.Rules.Add(new ScopeXViewBoundsRule(this, scope.Axes.Bottom));
        _scopePlot.Refresh();

        Plot rate = _ratePlot.Plot;
        rate.Clear();
        ApplyPlotTheme(rate);
        rate.Axes.SetLimitsY(-rateErrorYScale, rateErrorYScale);
        rate.Axes.SetLimitsX(0, rateDataPoints);
        ClearSeriesData(_rateX, _rateY);
        AddRatePlottables();
        PlotAxisRules.ClampLeftEdgeToZero(rate);
        _ratePlot.Refresh();
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _sampleRate = context.SampleRate;

        bool scopeUpdated = ReplaceScopeSeries(frame);
        bool rateUpdated = ReplaceRateSeries(frame);
        // Review cursor on the waveform pane only: its x base is absolute sample
        // ticks, so stream time maps onto it (the Multi-Filter Scope mapping).
        // The rate pane plots a fixed beat-index ring (0..rateDataPoints), not
        // stream time, so the review-cursor contract has no meaningful x mapping
        // there.
        bool cursorMoved = UpdateReviewCursor(context);

        if (rateUpdated)
        {
            _ratePlot.Refresh();
        }

        if (scopeUpdated)
        {
            UpdateScopeMarkers(frame.VerticalMarkers, frame.HorizontalMarkers, frame.TextMarkers);

            // Refresh the drawn-data extent every frame (even while panned) so the
            // bounds rule confines pan/zoom to the current [oldest .. now] range and
            // the X-axis start point stays pinned to the data.
            _dataMaxX = frame.GraphTickEnd;
            _dataMinX = ScopeOldestTick();
            _hasDataExtent = _dataMaxX > _dataMinX;

            if (_scopeFollowLive)
            {
                // Default 500 ms live window; the user can zoom out to at most 2 s
                // (ScopeXViewBoundsRule caps the span at the retained extent).
                double width = DefaultScopeWindowSeconds * context.SampleRate;
                double end = frame.GraphTickEnd;
                _scopePlot.Plot.Axes.SetLimitsX(end - width, end);
                _scopePlot.Plot.Axes.AutoScaleY();
            }

            // Re-lay the fixed 0.2 s ruler over whatever X range is now shown.
            ApplyScopeTimeTicks();
        }

        if (scopeUpdated || cursorMoved)
        {
            _scopePlot.Refresh();
        }
    }

    /// <summary>Review-cursor contract: a dotted marker at the scrub time on the waveform pane.</summary>
    private bool UpdateReviewCursor(AnalysisTabRenderContext context)
    {
        if (_scopeReviewCursor == null)
        {
            return false;
        }

        return _scopeReviewCursor.Update(context.ReviewCursorTimeS * context.SampleRate);
    }

    private ReviewCursorLayer AddReviewCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    /// <summary>Resets the rate plot (top) to its configured limits.</summary>
    public void ResetRateView()
    {
        _ratePlot.Plot.Axes.SetLimitsY(-_rateErrorYScale, _rateErrorYScale);
        _ratePlot.Plot.Axes.SetLimitsX(0, _rateDataPoints);
        _ratePlot.Refresh();
    }

    /// <summary>Restores the scope plot (bottom): re-arms live auto-follow and refits.</summary>
    public void ResetScopeView()
    {
        _scopeFollowLive = true;
        _scopePlot.Plot.Axes.AutoScale();
        ApplyScopeTimeTicks();
        _scopePlot.Refresh();
    }

    /// <summary>Oldest sample tick currently held across the scope series (0 when empty).</summary>
    private double ScopeOldestTick()
    {
        double oldest = double.MaxValue;
        for (int i = 0; i < _scopeX.Length; i++)
        {
            if (_scopeX[i].Count > 0 && _scopeX[i][0] < oldest)
            {
                oldest = _scopeX[i][0];
            }
        }

        return oldest == double.MaxValue ? 0.0 : oldest;
    }

    /// <summary>
    /// Confines the scope's X view to the live drawn-data extent
    /// (<see cref="_dataMinX"/>..<see cref="_dataMaxX"/>), refreshed each frame. A
    /// pan/zoom-out that would carry the view past either end is shifted back inside
    /// (span preserved); a view wider than the data snaps to the full range. This
    /// lets the user swipe/zoom within the retained window while the X-axis start
    /// point can never drift off the data. No data yet (Max ≤ Min) is a no-op.
    /// </summary>
    private sealed class ScopeXViewBoundsRule : IAxisRule
    {
        private readonly RateScopeRenderer _owner;
        private readonly IXAxis _xAxis;

        public ScopeXViewBoundsRule(RateScopeRenderer owner, IXAxis xAxis)
        {
            _owner = owner;
            _xAxis = xAxis;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            if (!_owner._hasDataExtent)
            {
                return;
            }

            double min = _owner._dataMinX;
            double max = _owner._dataMaxX;
            double extent = max - min;
            if (extent <= 0.0)
            {
                return;
            }

            // Largest visible span = the smaller of the retained data and the 2 s
            // zoom-out ceiling, so the user can never show more than 2 s at once.
            double cap = Math.Min(extent, MaxScopeWindowSeconds * _owner._sampleRate);

            double left = _xAxis.Range.Min;
            double right = _xAxis.Range.Max;
            double span = right - left;
            if (span <= 0.0)
            {
                return;
            }

            if (span >= cap)
            {
                right = max;
                left = max - cap;
            }
            else
            {
                if (left < min)
                {
                    left = min;
                    right = min + span;
                }

                if (right > max)
                {
                    right = max;
                    left = max - span;
                }

                if (left < min)
                {
                    left = min;
                }
            }

            _xAxis.Range.Min = left;
            _xAxis.Range.Max = right;
        }
    }

    private bool ReplaceScopeSeries(AnalysisFrame frame)
    {
        bool updated = false;
        for (int i = 0; i < _scopeSeries.Length; i++)
        {
            GraphSeriesFrame? series = SeriesDataReducer.FindSeries(frame.ScopeSeries, _scopeSeries[i].Id);
            if (series == null)
            {
                continue;
            }

            updated |= SeriesDataReducer.TryReplaceSeriesData(series, _scopeX[i], _scopeY[i], _scopeSeries[i].TargetPointBudget);
        }

        return updated;
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

    private void AddScopePlottables()
    {
        Plot scope = _scopePlot.Plot;
        _scopePlots.Clear();
        for (int i = 0; i < _scopeSeries.Length; i++)
        {
            GraphSeriesDefinition spec = _scopeSeries[i];
            Scatter sc = scope.Add.Scatter(_scopeX[i], _scopeY[i]);
            sc.LineWidth = 1;
            sc.LineColor = Color.FromARGB(ThemeColor(spec));
            sc.MarkerStyle.IsVisible = false;
            if (spec.FillAlpha > 0)
            {
                sc.FillY = true;
                sc.FillYColor = Color.FromARGB(ThemeColor(spec)).WithAlpha(spec.FillAlpha);
            }
            sc.LegendText = spec.Name;
            _scopePlots.Add(sc);
        }
    }

    private void AddRatePlottables()
    {
        Plot rate = _ratePlot.Plot;
        _ratePlots.Clear();
        for (int i = 0; i < _rateSeries.Length; i++)
        {
            GraphSeriesDefinition spec = _rateSeries[i];
            Scatter sc = rate.Add.Scatter(_rateX[i], _rateY[i]);
            sc.LineWidth = 0;
            sc.MarkerShape = MarkerShape.FilledCircle;
            sc.MarkerSize = 6;
            sc.MarkerColor = Color.FromARGB(ThemeColor(spec));
            sc.LegendText = spec.Name;
            _ratePlots.Add(sc);
        }
    }

    private void ApplySeriesTheme()
    {
        for (int i = 0; i < _scopePlots.Count && i < _scopeSeries.Length; i++)
        {
            uint color = ThemeColor(_scopeSeries[i]);
            _scopePlots[i].LineColor = Color.FromARGB(color);
            if (_scopeSeries[i].FillAlpha > 0)
            {
                _scopePlots[i].FillYColor = Color.FromARGB(color).WithAlpha(_scopeSeries[i].FillAlpha);
            }
        }

        for (int i = 0; i < _ratePlots.Count && i < _rateSeries.Length; i++)
        {
            _ratePlots[i].MarkerColor = Color.FromARGB(ThemeColor(_rateSeries[i]));
        }

        for (int i = 0; i < _scopeLinePool.Count; i++)
        {
            _scopeLinePool[i].LineColor = Color.FromARGB(ThemeColor(_scopeLineSourceColors[i]));
        }

        for (int i = 0; i < _scopeTextPool.Count; i++)
        {
            _scopeTextPool[i].LabelFontColor = Color.FromARGB(ThemeColor(_scopeTextSourceColors[i]));
        }

        _scopeReviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);

        plot.Legend.BackgroundColor = Color.FromARGB(_theme.ScopeBg);
        plot.Legend.FontColor = Color.FromARGB(_theme.TextPrimary);
        plot.Legend.OutlineColor = Color.FromARGB(_theme.ScopeGrid);
    }

    private uint ThemeColor(GraphSeriesDefinition spec) => spec.Id switch
    {
        // Waveform = wave color; tick beats green; tock beats (and trigger) red.
        AnalysisGraphSeries.ScopePcm => _theme.TraceWave,
        AnalysisGraphSeries.ScopeThreshold => _theme.TraceTock,
        AnalysisGraphSeries.RateTic => _theme.TraceTick,
        AnalysisGraphSeries.RateToc => _theme.TraceTock,
        _ => _theme.TraceWave,
    };

    private static List<double>[] CreateSeriesLists(int count)
    {
        var lists = new List<double>[count];
        for (int i = 0; i < count; i++)
        {
            lists[i] = new List<double>();
        }

        return lists;
    }

    private static void ClearSeriesData(List<double>[] xs, List<double>[] ys)
    {
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i].Clear();
            ys[i].Clear();
        }
    }


    /// <summary>
    /// Lays a fixed-interval time ruler over the scope's current X view (X is in
    /// absolute sample ticks): a minor tick every 0.1 s and a labeled major tick
    /// every 0.2 s (label in seconds, one decimal). The uniform spacing keeps the
    /// grid stable regardless of the live window length. No-op before the sample
    /// rate is known.
    /// </summary>
    /// <summary>
    /// Re-lays the scope time ruler and refreshes after a user pan/zoom. Deferred to
    /// the UI thread so ScottPlot has applied the new X limits before they are read,
    /// and coalesced so a continuous drag queues a single recompute (latest wins).
    /// </summary>
    private void ScheduleScopeAxisRefresh()
    {
        if (_scopeAxisRefreshPending)
        {
            return;
        }

        _scopeAxisRefreshPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _scopeAxisRefreshPending = false;
                ApplyScopeTimeTicks();
                _scopePlot.Refresh();
            },
            DispatcherPriority.Background);
    }

    private void ApplyScopeTimeTicks()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        AxisLimits limits = _scopePlot.Plot.Axes.GetLimits();
        double left = limits.Left;
        double right = limits.Right;
        if (double.IsNaN(left) || double.IsNaN(right) || right <= left)
        {
            return;
        }

        double minorStep = 0.1 * _sampleRate;
        var ticks = new ScottPlot.TickGenerators.NumericManual();
        long firstTenth = (long)Math.Ceiling(left / minorStep - 1e-6);
        long lastTenth = (long)Math.Floor(right / minorStep + 1e-6);
        for (long tenth = firstTenth; tenth <= lastTenth; tenth++)
        {
            double position = tenth * minorStep;
            if (tenth % 2 == 0)
            {
                // Labeled major tick every 0.2 s.
                string label = (tenth / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
                ticks.AddMajor(position, label);
            }
            else
            {
                ticks.AddMinor(position); // 0.1 s — short mark, no label
            }
        }

        _scopePlot.Plot.Axes.Bottom.TickGenerator = ticks;
    }

    /// <summary>Pool cleanup for paths that already detached everything via Plot.Clear().</summary>
    private void DropScopeMarkerPool()
    {
        _scopeLinePool.Clear();
        _scopeTextPool.Clear();
        _scopeLineSourceColors.Clear();
        _scopeTextSourceColors.Clear();
        _scopeLinesUsed = 0;
        _scopeTextsUsed = 0;
    }

    internal void UpdateScopeMarkers(
        IReadOnlyList<ScopeVerticalMarker> verticalMarkers,
        IReadOnlyList<ScopeHorizontalMarker> horizontalMarkers,
        IReadOnlyList<ScopeTextMarker> textMarkers)
    {
        _scopeLinesUsed = 0;
        _scopeTextsUsed = 0;

        foreach (ScopeVerticalMarker marker in verticalMarkers)
        {
            AddVerticalMarker(marker.X, marker.Height, marker.Color);
        }

        foreach (ScopeHorizontalMarker marker in horizontalMarkers)
        {
            if (marker.Direction == HorizontalMarkerDirection.Inward)
            {
                AddHorizontalMarkerInward(marker.XLeft, marker.XRight, marker.Length, marker.Height, marker.Color);
            }
            else
            {
                AddHorizontalMarkerOutward(marker.XLeft, marker.XRight, marker.Height, marker.Color);
            }
        }

        foreach (ScopeTextMarker marker in textMarkers)
        {
            AddText(marker.X, marker.Height, marker.Text, marker.Color, marker.Alignment);
        }

        for (int i = _scopeLinesUsed; i < _scopeLinePool.Count; i++)
        {
            _scopeLinePool[i].IsVisible = false;
        }
        for (int i = _scopeTextsUsed; i < _scopeTextPool.Count; i++)
        {
            _scopeTextPool[i].IsVisible = false;
        }
    }

    private LinePlot AcquireLine(uint sourceColor)
    {
        if (_scopeLinesUsed < _scopeLinePool.Count)
        {
            LinePlot pooled = _scopeLinePool[_scopeLinesUsed];
            _scopeLineSourceColors[_scopeLinesUsed] = sourceColor;
            _scopeLinesUsed++;
            pooled.IsVisible = true;
            return pooled;
        }

        LinePlot created = _scopePlot.Plot.Add.Line(0.0, 0.0, 0.0, 0.0);
        created.MarkerStyle.IsVisible = false;
        _scopeLinePool.Add(created);
        _scopeLineSourceColors.Add(sourceColor);
        _scopeLinesUsed++;
        return created;
    }

    private Text AcquireText(uint sourceColor)
    {
        if (_scopeTextsUsed < _scopeTextPool.Count)
        {
            Text pooled = _scopeTextPool[_scopeTextsUsed];
            _scopeTextSourceColors[_scopeTextsUsed] = sourceColor;
            _scopeTextsUsed++;
            pooled.IsVisible = true;
            return pooled;
        }

        Text created = _scopePlot.Plot.Add.Text("", 0.0, 0.0);
        created.LabelFontName = _textFontFamily;
        created.LabelFontSize = 10;
        _scopeTextPool.Add(created);
        _scopeTextSourceColors.Add(sourceColor);
        _scopeTextsUsed++;
        return created;
    }

    private void AddVerticalMarker(double x, double height, uint color)
    {
        LinePlot line = AcquireLine(color);
        line.Line = new CoordinateLine(x, 0.0, x, height);
        line.LineColor = Color.FromARGB(ThemeColor(color));
        line.LineWidth = 2;
        line.LinePattern = LinePattern.Dashed;
    }

    private void AddText(double x, double height, string text, uint color, MarkerTextAlignment alignment)
    {
        Text label = AcquireText(color);
        label.LabelText = text;
        label.Location = new Coordinates(x, height);
        label.LabelFontColor = Color.FromARGB(ThemeColor(color));
        label.Alignment = MapAlignment(alignment);
    }

    private static Alignment MapAlignment(MarkerTextAlignment alignment) => alignment switch
    {
        MarkerTextAlignment.CenterTop => Alignment.UpperCenter,
        MarkerTextAlignment.LeftTop => Alignment.UpperLeft,
        _ => Alignment.UpperLeft,
    };

    private void AddHorizontalMarkerInward(double xLeft, double xRight, double length, double height, uint color)
    {
        Color c = Color.FromARGB(ThemeColor(color));

        LinePlot left = AcquireLine(color);
        left.Line = new CoordinateLine(xLeft - length, height, xLeft, height);
        left.LineColor = c;
        left.LineWidth = 1;
        left.LinePattern = LinePattern.Solid;

        LinePlot right = AcquireLine(color);
        right.Line = new CoordinateLine(xRight, height, xRight + length, height);
        right.LineColor = c;
        right.LineWidth = 1;
        right.LinePattern = LinePattern.Solid;
    }

    private void AddHorizontalMarkerOutward(double xLeft, double xRight, double height, uint color)
    {
        LinePlot line = AcquireLine(color);
        line.Line = new CoordinateLine(xLeft, height, xRight, height);
        line.LineColor = Color.FromARGB(ThemeColor(color));
        line.LineWidth = 1;
        line.LinePattern = LinePattern.Solid;
    }

    private uint ThemeColor(uint sourceColor) => sourceColor switch
    {
        Argb.Green => _theme.TraceTick,
        Argb.Red => _theme.TraceTock,
        Argb.Black => _theme.TextPrimary,
        _ => sourceColor,
    };
}
