using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
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

    // Default width of the scope (Amplitude) window in seconds. The live view
    // shows this much of the most recent signal, always anchored to the live edge
    // (right = now). Change this one value to set the default span.
    private const double DefaultScopeWindowSeconds = 30.0;

    // Bounds and per-notch factor for the mouse-wheel span zoom: the wheel only
    // changes how many seconds are visible; the window stays anchored to now.
    // Max matches the retained buffer (ScopeRateFrameProjector.ScopeSnapshotSeconds).
    private const double MinScopeWindowSeconds = 0.05;
    private const double MaxScopeWindowSeconds = 30.0;
    private const double ScopeZoomStep = 1.2;

    // Current visible span (adjusted by the wheel) and the latest live-edge tick
    // (so the wheel can re-anchor immediately between frames).
    private double _scopeWindowSeconds = DefaultScopeWindowSeconds;
    private double _scopeEndTick;
    private double _scopeOldestTick;
    // Keeps the X view inside the held data [oldest .. now] so pan/zoom-out can't
    // drag past the graph start/end; its bounds are refreshed each frame.
    private ScopeXBoundaryRule? _scopeXBoundary;
    // True while the Y axis auto-fits the waveform; a Ctrl+wheel vertical zoom
    // turns it off so the manual Y range sticks (Reset View re-arms it).
    private bool _scopeAutoY = true;

    public RateScopeRenderer(AvaPlot scopePlot, AvaPlot ratePlot, string textFontFamily)
    {
        _scopePlot = scopePlot;
        _ratePlot = ratePlot;
        _textFontFamily = textFontFamily;

        // The Amplitude scope follows the live edge (right = latest sample) by
        // default. Left-drag pans horizontally and drops live-follow so the view
        // holds where dragged (Reset View re-arms it). The default cursor-centred
        // wheel zoom is replaced with a live-anchored span zoom (plain wheel) plus
        // a Ctrl+wheel vertical zoom.
        _scopePlot.UserInputProcessor.LeftClickDragPan(true, true, false);
        _scopePlot.UserInputProcessor.UserActionResponses.RemoveAll(
            r => r is ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom);
        _scopePlot.PointerMoved += (_, e) =>
        {
            if (e.GetCurrentPoint(_scopePlot).Properties.IsLeftButtonPressed)
            {
                _scopeFollowLive = false;
            }
        };
        _scopePlot.PointerWheelChanged += (_, e) =>
        {
            double factor = e.Delta.Y > 0 ? 1.0 / ScopeZoomStep : ScopeZoomStep;
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                // Ctrl+wheel: vertical zoom about the Y centre. Stop the auto-Y fit
                // so the manual range persists across live frames.
                _scopeAutoY = false;
                AxisLimits limits = _scopePlot.Plot.Axes.GetLimits();
                double centre = (limits.Top + limits.Bottom) / 2.0;
                double half = (limits.Top - limits.Bottom) / 2.0 * factor;
                _scopePlot.Plot.Axes.SetLimitsY(centre - half, centre + half);
            }
            else
            {
                // Plain wheel: re-arm live follow and zoom the horizontal span,
                // capped at the data actually held so zooming out never reveals
                // empty time before the oldest sample (the bunched-right view).
                _scopeFollowLive = true;
                double dataSpanSec = (_scopeEndTick - _scopeOldestTick) / Math.Max(1, _sampleRate);
                double maxSec = Math.Clamp(dataSpanSec, MinScopeWindowSeconds, MaxScopeWindowSeconds);
                _scopeWindowSeconds = Math.Clamp(_scopeWindowSeconds * factor, MinScopeWindowSeconds, maxSec);
                ApplyScopeWindow();
            }

            _scopePlot.Refresh();
            e.Handled = true;
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
        _scopeWindowSeconds = DefaultScopeWindowSeconds;
        _scopeAutoY = true;
        Plot scope = _scopePlot.Plot;
        scope.Clear();
        ApplyPlotTheme(scope);
        scope.YLabel("Amplitude");
        scope.XLabel("Time (mm:ss.fff)");
        scope.Axes.SetLimitsY(0, 0.1);
        scope.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = ScopeTickToMs
        };
        ClearSeriesData(_scopeX, _scopeY);
        DropScopeMarkerPool();
        AddScopePlottables();
        _scopeReviewCursor = AddReviewCursor(scope);
        scope.ShowLegend();
        scope.Axes.Rules.Clear();
        _scopeXBoundary = new ScopeXBoundaryRule(scope.Axes.Bottom);
        scope.Axes.Rules.Add(_scopeXBoundary);

        Plot rate = _ratePlot.Plot;
        rate.Clear();
        ApplyPlotTheme(rate);
        rate.YLabel("Rate Error (ms)");
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
        _scopeWindowSeconds = DefaultScopeWindowSeconds;
        _scopeAutoY = true;
        Plot scope = _scopePlot.Plot;
        scope.Clear();
        ApplyPlotTheme(scope);
        ClearSeriesData(_scopeX, _scopeY);
        DropScopeMarkerPool();
        AddScopePlottables();
        _scopeReviewCursor = AddReviewCursor(scope);
        scope.Axes.Rules.Clear();
        _scopeXBoundary = new ScopeXBoundaryRule(scope.Axes.Bottom);
        scope.Axes.Rules.Add(_scopeXBoundary);
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
            // Refresh the held-data extent every frame (even while panned) so the
            // boundary rule confines pan/zoom to the current [oldest .. now] range.
            _scopeEndTick = frame.GraphTickEnd;
            _scopeOldestTick = ScopeOldestTick();
            if (_scopeXBoundary != null)
            {
                _scopeXBoundary.Min = _scopeOldestTick;
                _scopeXBoundary.Max = _scopeEndTick;
            }

            if (_scopeFollowLive)
            {
                // Live-anchored window of the last _scopeWindowSeconds; labels read
                // from 00:00 at the window start.
                ApplyScopeWindow();
            }
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

    /// <summary>Restores the scope plot (bottom): re-arms live auto-follow and the default span.</summary>
    public void ResetScopeView()
    {
        _scopeFollowLive = true;
        _scopeWindowSeconds = DefaultScopeWindowSeconds;
        _scopeAutoY = true;
        _scopePlot.Plot.Axes.AutoScale();
        _scopePlot.Refresh();
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
            sc.MarkerSize = 3;
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
    /// Applies the live window: a span of <see cref="_scopeWindowSeconds"/> anchored
    /// to the latest sample (right = now), clamped so the left edge never goes
    /// before the oldest sample held — no empty time, no bunched-right view.
    /// </summary>
    private void ApplyScopeWindow()
    {
        double maxWidth = Math.Max(1.0, _scopeEndTick - _scopeOldestTick);
        double width = Math.Min(_scopeWindowSeconds * Math.Max(1, _sampleRate), maxWidth);
        _scopePlot.Plot.Axes.SetLimitsX(_scopeEndTick - width, _scopeEndTick);
        if (_scopeAutoY)
        {
            _scopePlot.Plot.Axes.AutoScaleY();
        }
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
    /// Confines the scope's X view to the held-data range [<see cref="Min"/> ..
    /// <see cref="Max"/>], refreshed each frame. A pan/zoom-out that would carry
    /// the view past either end is shifted back inside (span preserved); a view
    /// wider than the data snaps to the full range. No data yet (Max ≤ Min) is a
    /// no-op so the empty plot is left alone.
    /// </summary>
    private sealed class ScopeXBoundaryRule : ScottPlot.IAxisRule
    {
        private readonly ScottPlot.IXAxis _xAxis;

        public double Min;
        public double Max;

        public ScopeXBoundaryRule(ScottPlot.IXAxis xAxis)
        {
            _xAxis = xAxis;
        }

        public void Apply(ScottPlot.RenderPack rp, bool beforeLayout)
        {
            if (Max <= Min)
            {
                return;
            }

            double min = _xAxis.Range.Min;
            double max = _xAxis.Range.Max;
            double span = max - min;
            if (span <= 0)
            {
                return;
            }

            if (span >= Max - Min)
            {
                _xAxis.Range.Min = Min;
                _xAxis.Range.Max = Max;
            }
            else if (min < Min)
            {
                _xAxis.Range.Min = Min;
                _xAxis.Range.Max = Min + span;
            }
            else if (max > Max)
            {
                _xAxis.Range.Max = Max;
                _xAxis.Range.Min = Max - span;
            }
        }
    }

    private string ScopeTickToMs(double sampleTick)
    {
        // Absolute capture time of the sample (ticks since the run started), so a
        // given point always reads the same mm:ss.fff regardless of pan/zoom —
        // zooming changes what is visible, never a point's timestamp.
        double ms = sampleTick / Math.Max(1, _sampleRate) * 1000.0;
        if (ms < 0)
        {
            ms = 0;
        }

        return TimeSpan.FromMilliseconds(ms).ToString(@"mm\:ss\.fff");
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
