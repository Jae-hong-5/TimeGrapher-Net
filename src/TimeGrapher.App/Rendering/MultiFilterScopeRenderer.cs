using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Multi-Filter Scope: four vertically stacked views (F0..F3, one lane per
/// <see cref="MultiFilterScopeLanes"/> entry) of the same signal, refilled in
/// place by series id from the frame's Core-decimated replace snapshots so the
/// four filters are easy to switch between and compare. Each plot windows its
/// X axis to the last two seconds of its own series (x = absolute sample ticks
/// on the projector's counter, so limits derive from the series' own max x).
/// The four lanes share one X base, so a user zoom/pan on any lane is linked
/// onto the other three — every lane always reads on the same timestamp window.
/// Honors the review-cursor contract by mapping the scrubbed stream time to
/// sample ticks on every lane.
/// </summary>
internal sealed class MultiFilterScopeRenderer
{
    // Semi-opaque baseline fill for the mirrored lanes, so the symmetric
    // envelope reads as a solid band rather than a thin outline.
    private const byte MirrorFillAlpha = 150;

    private readonly AvaPlot[] _plots;
    private readonly List<double>[] _x;
    private readonly List<double>[] _y;
    private readonly Scatter?[] _scatters;
    private readonly ReviewCursorLayer?[] _cursors;

    // Mirror traces for the mirrored lanes (F0..F2): a second scatter on the
    // negated y so the envelope shows symmetrically above and below the
    // baseline (PC-RM4 FR-12-07). _yMirror[i] is refilled in place from _y[i]
    // whenever the lane's data is replaced. Null for one-sided lanes (F3).
    private readonly List<double>[] _yMirror;
    private readonly Scatter?[] _mirrorScatters;

    // Identity gate on the projector's shared-instance pattern: between
    // publish-floor rebuilds (and on every paused-scrub re-route) frames
    // re-attach the same immutable GraphSeriesFrame per lane, and a rebuild
    // always allocates new ones — so reference equality is a correct change
    // detector and skips the redundant copy/rescale/refresh.
    private readonly GraphSeriesFrame?[] _lastSeries;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private bool _followLive = true;

    // Re-entrancy guard for the X-axis link: propagating limits onto the other
    // lanes must not re-trigger a sync from them.
    private bool _syncing;

    // Coalescing gate for the X-axis link: a drag fires PointerMoved many times
    // per second, but only one deferred sync needs to be queued — it reads the
    // latest limits when it runs. While one post is pending, later interactions
    // just update _syncSource (latest wins) instead of queuing another full
    // 3-lane SetLimitsX/AutoScaleY/Refresh pass.
    private bool _syncPending;
    private int _syncSource;

    // Whether the pending sync should re-derive the shared Y pan offset from the
    // source lane: a drag sets it (the vertical pan is shared across all lanes);
    // a wheel leaves it false (a wheel changes only the zoom, not the offset).
    private bool _syncDeriveYOffset;

    // Shared Y-zoom level for all four lanes: the visible Y half-range as a
    // multiple of each lane's own data peak (the max input value). The wheel
    // adjusts it for every lane at once, and it is capped at MaxYZoom so a
    // zoom-out can never show more than 1.5x the peak (e.g. a 0.08 peak tops
    // out at 0.12). Y stays per-lane in scale — each lane uses its own peak.
    private const double DefaultYZoom = 1.1;  // fit with a little headroom
    private const double MaxYZoom = 1.5;      // zoom-out cap: <= 1.5x the peak
    private const double MinYZoom = 0.1;      // zoom-in floor
    private const double YZoomStep = 1.15;    // per wheel notch
    private double _yZoom = DefaultYZoom;

    // Shared Y pan offset for all lanes, in units of each lane's data peak
    // (0 = centered). A vertical drag updates it so every lane pans together;
    // the one-sided lane (F3) additionally never shows below 0.
    private double _yOffset;

    // Live extent of the drawn data on the shared X base (oldest..latest
    // retained tick), refreshed each frame. The X view is clamped to stay
    // within it — a pan stops at the first/last drawn sample with no empty
    // margin beyond the data, and zoom-out is capped at the full buffer. When
    // the view spans the whole extent, live auto-follow re-arms so it advances.
    private double _dataMinX;
    private double _dataMaxX;
    private bool _hasDataExtent;

    // Sample rate of the current run (X is in absolute sample ticks), used to
    // label the bottom axis in seconds with one tick per second.
    private int _sampleRate;

    // Visible window length. The producer retains a longer buffer
    // (MultiFilterFrameProjector.WindowSeconds), so the shown window's left edge
    // always sits on retained data and the rolling trim churn stays off-screen.
    private const double DisplayWindowSeconds = 1.0;

    public MultiFilterScopeRenderer(IReadOnlyList<AvaPlot> plots)
    {
        if (plots.Count != MultiFilterScopeLanes.All.Count)
        {
            throw new ArgumentException(
                $"Expected one plot per filter lane ({MultiFilterScopeLanes.All.Count}).", nameof(plots));
        }

        _plots = plots.ToArray();
        _x = new List<double>[_plots.Length];
        _y = new List<double>[_plots.Length];
        _scatters = new Scatter?[_plots.Length];
        _cursors = new ReviewCursorLayer?[_plots.Length];
        _lastSeries = new GraphSeriesFrame?[_plots.Length];
        _yMirror = new List<double>[_plots.Length];
        _mirrorScatters = new Scatter?[_plots.Length];

        for (int i = 0; i < _plots.Length; i++)
        {
            _x[i] = new List<double>();
            _y[i] = new List<double>();
            _yMirror[i] = new List<double>();

            int idx = i;
            // Any zoom (wheel), drag (pan / zoom-rectangle, while a button is
            // held), or interaction end re-links the other lanes onto this one's
            // X window. PointerPressed just drops live-follow; it changes no axis.
            _plots[idx].PointerPressed += (_, _) => _followLive = false;
            _plots[idx].PointerWheelChanged += (_, e) =>
            {
                // Wheel down (Delta.Y < 0) zooms out, up zooms in — applied to
                // every lane through the shared _yZoom and capped at MaxYZoom.
                if (e.Delta.Y < 0)
                {
                    _yZoom = Math.Min(MaxYZoom, _yZoom * YZoomStep);
                }
                else if (e.Delta.Y > 0)
                {
                    _yZoom = Math.Max(MinYZoom, _yZoom / YZoomStep);
                }

                OnUserAxisInteraction(idx, deriveYOffset: false);
            };
            // Drag (pan): sync X and re-derive the shared Y offset so a vertical
            // drag pans every lane together.
            _plots[idx].PointerReleased += (_, _) => OnUserAxisInteraction(idx, deriveYOffset: true);
            _plots[idx].PointerMoved += (_, e) =>
            {
                Avalonia.Input.PointerPointProperties props = e.GetCurrentPoint(_plots[idx]).Properties;
                if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
                {
                    OnUserAxisInteraction(idx, deriveYOffset: true);
                }
            };

            // ScottPlot's built-in double-click toggles the benchmark overlay, and
            // it counts two quick drags as a double-click — so fast panning keeps
            // flashing it on. Drop that response and instead toggle the benchmark
            // only on a genuine Avalonia double-tap (which a drag, having moved,
            // never raises).
            _plots[idx].UserInputProcessor.UserActionResponses.RemoveAll(
                response => response.GetType().Name.Contains("Benchmark", StringComparison.Ordinal));
            _plots[idx].DoubleTapped += (_, _) =>
            {
                Plot plot = _plots[idx].Plot;
                plot.Benchmark.IsVisible = !plot.Benchmark.IsVisible;
                _plots[idx].Refresh();
            };

            // The default right-click "Auto Scale" fits only the clicked lane,
            // which breaks the shared timestamp window. Replace the menu with an
            // all-lanes auto scale routed through ResetView so every lane fits
            // together (matching the Reset View button).
            _plots[idx].Menu?.Clear();
            _plots[idx].Menu?.Add("Auto Scale (all lanes)", _ => ResetView());
        }
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        for (int i = 0; i < _plots.Length; i++)
        {
            ApplyPlotTheme(_plots[i].Plot);
        }

        ApplySeriesTheme();
        RefreshAll();
    }

    public void CreateGraphs()
    {
        _followLive = true;
        _hasDataExtent = false;
        _yZoom = DefaultYZoom;
        _yOffset = 0.0;
        for (int i = 0; i < _plots.Length; i++)
        {
            Plot plot = _plots[i].Plot;
            plot.Clear();
            _x[i].Clear();
            _y[i].Clear();
            _yMirror[i].Clear();
            _mirrorScatters[i] = null;
            _lastSeries[i] = null;
            ApplyPlotTheme(plot);
            plot.YLabel(MultiFilterScopeLanes.All[i].Label);
            // Time ruler: a minor tick every 0.1 s, a longer major tick every
            // 0.5 s, and a number label only on whole seconds. Same on every lane.
            plot.Axes.Bottom.TickLabelStyle.IsVisible = true;
            plot.Axes.Bottom.MinorTickStyle.Length = 3;
            plot.Axes.Bottom.MajorTickStyle.Length = 6;
            _scatters[i] = plot.Add.Scatter(_x[i], _y[i]);
            _scatters[i]!.LineWidth = 1;
            _scatters[i]!.MarkerStyle.IsVisible = false;
            if (MultiFilterScopeLanes.All[i].Mirrored)
            {
                // Fill each half to the baseline (0): the +y trace fills upward
                // and the -y mirror fills downward, so together they read as a
                // solid symmetric envelope (the PC-RM4 filled-waveform look).
                _scatters[i]!.FillY = true;
                _mirrorScatters[i] = plot.Add.Scatter(_x[i], _yMirror[i]);
                _mirrorScatters[i]!.LineWidth = 1;
                _mirrorScatters[i]!.MarkerStyle.IsVisible = false;
                _mirrorScatters[i]!.FillY = true;
            }

            _cursors[i] = AddCursor(plot);
            // Clamp the X view to the live drawn-data extent: a left/right pan
            // stops at the oldest/latest sample (no empty region beyond the
            // data) and zoom-out is capped at the full buffer. The extent rolls
            // with the window, so the rule reads it live on every render.
            plot.Axes.Rules.Clear();
            plot.Axes.Rules.Add(new XViewBoundsRule(this, plot.Axes.Bottom));
        }

        ApplySeriesTheme();
        RefreshAll();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    /// <summary>
    /// Re-arms live windowing on all four lanes after a pan/zoom (the one-way
    /// follow-live latch otherwise sticks until the session restarts).
    /// </summary>
    public void ResetView()
    {
        _followLive = true;
        _yZoom = DefaultYZoom;
        _yOffset = 0.0;
        for (int i = 0; i < _plots.Length; i++)
        {
            _plots[i].Plot.Axes.AutoScale();
            ApplyY(i);
            AxisLimits limits = _plots[i].Plot.Axes.GetLimits();
            ApplyTimeTicks(i, limits.Left, limits.Right);
        }

        RefreshAll();
    }

    /// <summary>
    /// Links a user zoom/pan on one lane onto the others: drops live-follow and
    /// copies this lane's X window to the rest so all four stay on the same
    /// timestamp range. Deferred so ScottPlot has applied the new limits to the
    /// source plot before they are read, and coalesced so a continuous drag
    /// queues a single sync (reading the latest limits) instead of one per
    /// PointerMoved.
    /// </summary>
    private void OnUserAxisInteraction(int source, bool deriveYOffset)
    {
        _followLive = false;
        _syncSource = source;            // latest interaction wins when the pending post runs
        _syncDeriveYOffset |= deriveYOffset; // any coalesced drag re-derives the Y offset
        if (_syncPending)
        {
            return;
        }

        _syncPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _syncPending = false;
                bool derive = _syncDeriveYOffset;
                _syncDeriveYOffset = false;
                SyncXAxisFrom(_syncSource, derive);
            },
            DispatcherPriority.Background);
    }

    private void SyncXAxisFrom(int source, bool deriveYOffset)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            // The four lanes share one X base (absolute sample ticks), so the
            // X window transfers directly. Keep it inside the drawn-data extent
            // (no empty margins): a zoom-out to the full buffer snaps to the
            // whole extent and re-arms live follow; otherwise the window is
            // shifted back inside the data. Y is set on every lane from the
            // shared _yZoom level (overriding the wheel's own Y zoom), each
            // scaled to its own data peak.
            AxisLimits limits = _plots[source].Plot.Axes.GetLimits();
            double left = limits.Left;
            double right = limits.Right;
            if (_hasDataExtent)
            {
                double extent = _dataMaxX - _dataMinX;
                double span = right - left;
                if (extent > 0.0 && span >= extent)
                {
                    _followLive = true;
                    _yOffset = 0.0; // full zoom-out re-centers Y as well
                    left = _dataMinX;
                    right = _dataMaxX;
                }
                else if (extent > 0.0)
                {
                    if (left < _dataMinX)
                    {
                        left = _dataMinX;
                        right = _dataMinX + span;
                    }

                    if (right > _dataMaxX)
                    {
                        right = _dataMaxX;
                        left = _dataMaxX - span;
                    }

                    if (left < _dataMinX)
                    {
                        left = _dataMinX;
                    }
                }
            }

            // A drag re-derives the shared Y offset from the source lane (a
            // mirrored, centered lane only — F3 is pinned at 0 and never the
            // source of a pan offset), so a vertical drag pans every lane.
            if (deriveYOffset && MultiFilterScopeLanes.All[source].Mirrored)
            {
                double peak = LanePeak(source);
                if (peak > 0.0)
                {
                    double center = (limits.Top + limits.Bottom) / 2.0;
                    _yOffset = Math.Clamp(center / peak, -MaxYZoom, MaxYZoom);
                }
            }

            for (int i = 0; i < _plots.Length; i++)
            {
                _plots[i].Plot.Axes.SetLimitsX(left, right);
                ApplyY(i);
                ApplyTimeTicks(i, left, right);
                _plots[i].Refresh();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Puts a time ruler on the lane's bottom axis: a minor tick every 0.1 s, a
    /// longer major tick every 0.5 s, and a seconds label on whole seconds only
    /// (X is absolute sample ticks, so 0.1 s = 0.1 * sampleRate). All lanes share
    /// the same positions for aligned gridlines. No-op before the rate is known.
    /// </summary>
    private void ApplyTimeTicks(int lane, double left, double right)
    {
        if (_sampleRate <= 0)
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
            if (tenth % 10 == 0)
            {
                ticks.AddMajor(position, $"{tenth / 10}s"); // whole second — labeled
            }
            else if (tenth % 5 == 0)
            {
                ticks.AddMajor(position, string.Empty); // 0.5 s — longer mark, no label
            }
            else
            {
                ticks.AddMinor(position); // 0.1 s — short mark
            }
        }

        _plots[lane].Plot.Axes.Bottom.TickGenerator = ticks;
    }

    /// <summary>Largest absolute sample in a lane's current display buffer (its data peak).</summary>
    private double LanePeak(int lane)
    {
        List<double> y = _y[lane];
        double peak = 0.0;
        for (int i = 0; i < y.Count; i++)
        {
            double magnitude = Math.Abs(y[i]);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }

    /// <summary>
    /// Sets a lane's Y view, scaled to that lane's own data peak (the max input
    /// value). Mirrored lanes are centered at <see cref="_yOffset"/> * peak with
    /// a half-height of <see cref="_yZoom"/> * peak (capped at 1.5x), so a
    /// vertical pan moves them. The one-sided lane (F3) is the exception: it is
    /// pinned to [0, _yZoom * peak] — it never shows below 0 and the pan offset
    /// does not apply, so a vertical drag leaves it unchanged (only a wheel zoom
    /// resizes it).
    /// </summary>
    private void ApplyY(int lane)
    {
        double peak = LanePeak(lane);
        if (peak <= 0.0)
        {
            return;
        }

        double half = _yZoom * peak;
        if (MultiFilterScopeLanes.All[lane].Mirrored)
        {
            double center = _yOffset * peak;
            _plots[lane].Plot.Axes.SetLimitsY(center - half, center + half);
        }
        else
        {
            _plots[lane].Plot.Axes.SetLimitsY(0.0, half);
        }
    }

    /// <summary>
    /// Clamps a lane's X view to the owner's live drawn-data extent
    /// (<see cref="_dataMinX"/>..<see cref="_dataMaxX"/>): a pan stops at the
    /// oldest/latest sample with no empty margin beyond the data, and a span at
    /// or past the full extent snaps to it. The extent rolls with the window, so
    /// it is read live on every render (a hard wall even mid-drag, unlike the
    /// deferred sync). Mirrors the shift-in-bounds logic in SyncXAxisFrom.
    /// </summary>
    private sealed class XViewBoundsRule : IAxisRule
    {
        private readonly MultiFilterScopeRenderer _owner;
        private readonly IXAxis _xAxis;

        public XViewBoundsRule(MultiFilterScopeRenderer owner, IXAxis xAxis)
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

            double left = _xAxis.Range.Min;
            double right = _xAxis.Range.Max;
            double span = right - left;
            if (span >= extent)
            {
                left = min;
                right = max;
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

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _sampleRate = context.SampleRate;

        // Replace each lane's data first, so the live extent below is computed
        // from THIS frame's data. Computing it before the replace would window on
        // the previous frame (one frame stale) and leave the first frame after a
        // reset at the empty 0..0 extent.
        var updated = new bool[_plots.Length];
        for (int i = 0; i < _plots.Length; i++)
        {
            GraphSeriesFrame? laneSeries = SeriesDataReducer.FindSeries(
                frame.ScopeSeries, MultiFilterScopeLanes.All[i].SeriesId);
            updated[i] = !ReferenceEquals(laneSeries, _lastSeries[i]) &&
                SeriesDataReducer.TryReplaceSeriesData(
                    laneSeries, _x[i], _y[i], MultiFilterFrameProjector.FilterPointBudget);
            if (updated[i])
            {
                _lastSeries[i] = laneSeries;
                UpdateMirror(i);
            }
        }

        if (_x[0].Count > 0)
        {
            // The view extent is the most recent DisplayWindowSeconds, not the
            // whole retained buffer: the older retained columns stay off the left
            // as lead-in so the visible left edge always has signal. Clamp to the
            // oldest available so an early (not-yet-full) run still fills in.
            _dataMaxX = _x[0][^1];
            double displaySamples = DisplayWindowSeconds * _sampleRate;
            _dataMinX = Math.Max(_x[0][0], _dataMaxX - displaySamples);
            _hasDataExtent = true;
        }
        else
        {
            _hasDataExtent = false;
        }

        for (int i = 0; i < _plots.Length; i++)
        {
            bool cursorMoved = UpdateReviewCursor(i, context);

            if (updated[i] && _x[i].Count > 0 && _followLive)
            {
                // Auto-follow shows the recent display window (the retained buffer
                // extends further left as off-screen lead-in), advancing as it
                // rolls, with Y fit at the shared zoom. When not following (the
                // user zoomed/panned), X is kept inside the data by the axis rule
                // and Y is left to the user's pan.
                _plots[i].Plot.Axes.SetLimitsX(_dataMinX, _dataMaxX);
                ApplyY(i);
            }

            if (updated[i] || cursorMoved)
            {
                AxisLimits limits = _plots[i].Plot.Axes.GetLimits();
                ApplyTimeTicks(i, limits.Left, limits.Right);
                _plots[i].Refresh();
            }
        }
    }

    /// <summary>Mirrors the lane's trace below the baseline by negating its y into the mirror list.</summary>
    private void UpdateMirror(int lane)
    {
        if (_mirrorScatters[lane] is null)
        {
            return;
        }

        List<double> y = _y[lane];
        List<double> mirror = _yMirror[lane];
        mirror.Clear();
        for (int i = 0; i < y.Count; i++)
        {
            mirror.Add(-y[i]);
        }
    }

    /// <summary>Review-cursor contract: a dotted marker at the scrub time on every lane.</summary>
    private bool UpdateReviewCursor(int lane, AnalysisTabRenderContext context)
    {
        // The lanes plot absolute sample ticks; map the stream time onto them.
        return _cursors[lane]?.Update(context.ReviewCursorTimeS * context.SampleRate) ?? false;
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private void ApplySeriesTheme()
    {
        for (int i = 0; i < _plots.Length; i++)
        {
            uint laneColor = MultiFilterScopeLanes.All[i].Color(_theme);
            Color fill = Color.FromARGB(laneColor).WithAlpha(MirrorFillAlpha);
            if (_scatters[i] is { } scatter)
            {
                scatter.LineColor = Color.FromARGB(laneColor);
                scatter.FillYColor = fill;
            }

            if (_mirrorScatters[i] is { } mirror)
            {
                mirror.LineColor = Color.FromARGB(laneColor);
                mirror.FillYColor = fill;
            }

            _cursors[i]?.ApplyTheme(_theme);
        }
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
    }

    private void RefreshAll()
    {
        for (int i = 0; i < _plots.Length; i++)
        {
            _plots[i].Refresh();
        }
    }

}
