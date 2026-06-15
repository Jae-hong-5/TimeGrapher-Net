using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
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
            _plots[idx].PointerWheelChanged += (_, _) => OnUserAxisInteraction(idx);
            _plots[idx].PointerReleased += (_, _) => OnUserAxisInteraction(idx);
            _plots[idx].PointerMoved += (_, e) =>
            {
                Avalonia.Input.PointerPointProperties props = e.GetCurrentPoint(_plots[idx]).Properties;
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
            plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
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
            // Wall (span-preserving) floor, not the plain left floor: the four
            // lanes share one linked X window, so a left pan that collapsed a
            // lane past the origin would be propagated to the others. The wall
            // stops the pan at the origin and keeps the window valid.
            PlotAxisRules.ClampLeftEdgePreservingSpan(plot);
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
        for (int i = 0; i < _plots.Length; i++)
        {
            _plots[i].Plot.Axes.AutoScale();
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
            // The four lanes share one X base (absolute sample ticks), so the
            // X limits transfer directly. Y stays per-lane — each filter has its
            // own amplitude — auto-scaled to the data now in the shared window.
            AxisLimits limits = _plots[source].Plot.Axes.GetLimits();
            for (int i = 0; i < _plots.Length; i++)
            {
                if (i == source)
                {
                    continue;
                }

                _plots[i].Plot.Axes.SetLimitsX(limits.Left, limits.Right);
                _plots[i].Plot.Axes.AutoScaleY();
                _plots[i].Refresh();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        for (int i = 0; i < _plots.Length; i++)
        {
            GraphSeriesFrame? laneSeries = SeriesDataReducer.FindSeries(
                frame.ScopeSeries, MultiFilterScopeLanes.All[i].SeriesId);
            bool updated = !ReferenceEquals(laneSeries, _lastSeries[i]) &&
                SeriesDataReducer.TryReplaceSeriesData(
                    laneSeries, _x[i], _y[i], MultiFilterFrameProjector.FilterPointBudget);
            if (updated)
            {
                _lastSeries[i] = laneSeries;
                UpdateMirror(i);
            }

            bool cursorMoved = UpdateReviewCursor(i, context);

            if (updated && _followLive && _x[i].Count > 0)
            {
                // Window to the last 2 s of this lane's own x base (sample ticks).
                double end = _x[i][^1];
                double width = (double)MultiFilterFrameProjector.WindowSeconds * context.SampleRate;
                _plots[i].Plot.Axes.SetLimitsX(end - width, end);
                _plots[i].Plot.Axes.AutoScaleY();
            }

            if (updated || cursorMoved)
            {
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
