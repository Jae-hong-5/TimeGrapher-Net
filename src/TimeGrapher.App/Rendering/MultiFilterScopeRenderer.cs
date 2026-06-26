using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

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

    // The view always follows the live beat-locked window: there is no mouse
    // pan/zoom to drop it, so it only re-arms (stays true) and never latches off.
    private bool _followLive = true;

    // Live extent of the drawn data on the shared X base (oldest..latest
    // retained tick), refreshed each frame. The X view is clamped to stay
    // within it — a pan stops at the first/last drawn sample with no empty
    // margin beyond the data, and zoom-out is capped at the full buffer. When
    // the view spans the whole extent, live auto-follow re-arms so it advances.
    private double _dataMinX;
    private double _dataMaxX;
    private bool _hasDataExtent;

    // Sticky rightmost ms tick (hysteresis): the beat window width jitters by a
    // fraction of a ms each beat, which would otherwise toggle the last tick
    // label on and off at a tick boundary. The window's right edge is held to
    // this value (with a half-step deadband) so a shown label stays until the
    // span genuinely moves to the next/previous tick. Reset per run.
    private double _stickyMaxTickMs;

    // Sample rate of the current run (X is in absolute sample ticks), used to
    // label the bottom axis in milliseconds from the window's left edge.
    private int _sampleRate;

    // Beat-locked window as a fraction of one beat period: less than a full period
    // so the beat impulse spreads across the width instead of being a thin spike.
    // Smaller = more zoom.
    private const double BeatWindowFraction = 0.2;

    // Hard cap on the beat-locked window width (ms): the X axis never spans more
    // than this, so a slow watch (long period) still tops out at a 100 ms view.
    private const double MaxWindowMs = 100.0;

    // Deadband (ms) for re-committing the beat-locked X window. Re-locking to the
    // live onset every frame nudged the window edges by the sub-ms beat-period
    // jitter, which re-scaled the axis and trembled the X-axis ticks on every
    // render. Hold the committed window until an edge moves more than this; a real
    // beat re-lock advances by ~one period and clears the band immediately.
    private const double WindowDeadbandMs = 0.5;

    // Where the beat onset sits in that window, as a fraction from the left edge:
    // a small pre-roll, so the impulse is left-aligned and its decay fills out to
    // the right (the PC-RM4 scope look), not centered.
    private const double BeatPrerollFraction = 0.1;

    // Per-lane running peak of the data (only grows within a run): the Y axis
    // scales to this so it never shrinks when a quieter beat passes, keeping the
    // amplitude scale steady. Reset each run.
    private readonly double[] _lanePeakMax;

    // Display decimation budget (the density slider): the producer emits the
    // envelope at its full budget; the renderer peak-decimates to this so the
    // user can trade render cost for resolution. The max is the producer budget
    // (no extra decimation); the min keeps the trace readable on the Pi.
    public const int MinDisplayBudget = 500;
    private int _displayBudget = MultiFilterFrameProjector.FilterPointBudget;

    // Y headroom over the running peak amplitude: the axis tops out at 1.2x the
    // tallest amplitude seen, so the waveform never clips, with a little room.
    private const double YHeadroom = 1.2;

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
        _lanePeakMax = new double[_plots.Length];

        for (int i = 0; i < _plots.Length; i++)
        {
            _x[i] = new List<double>();
            _y[i] = new List<double>();
            _yMirror[i] = new List<double>();

            int idx = i;
            // The scope is read-only and beat-locked, so drop every built-in
            // mouse response that moves the axes: the wheel zoom, the click-drag
            // pan and zoom-rectangle (any "Drag" response), and the double-click
            // benchmark toggle (two quick drags would trip it). The right-click
            // menu (no "Drag" in its name) is kept, and the benchmark is instead
            // toggled on a genuine Avalonia double-tap below.
            _plots[idx].UserInputProcessor.UserActionResponses.RemoveAll(response =>
            {
                string name = response.GetType().Name;
                return name.Contains("Wheel", StringComparison.Ordinal)
                    || name.Contains("Drag", StringComparison.Ordinal)
                    || name.Contains("Benchmark", StringComparison.Ordinal);
            });
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
        _dataMinX = 0.0;
        _dataMaxX = 0.0;
        _stickyMaxTickMs = 0.0;
        Array.Clear(_lanePeakMax);
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
            plot.XLabel(i >= _plots.Length - 2 ? "Time (ms)" : string.Empty);
            plot.YLabel(i % 2 == 0 ? "Signal Level (a.u.)" : string.Empty);
            // Time ruler: ms ticks measured from the window's left edge (see
            // ApplyTimeTicks). Same on every lane.
            plot.Axes.Bottom.TickLabelStyle.IsVisible = true;
            plot.Axes.Bottom.MajorTickStyle.Length = 6;
            // Axis panel sizes (incl. the bottom title row) are the shared compact
            // sizes applied in ApplyPlotTheme via PlotThemeHelper.ApplyCompactAxisPanels.
            // Start (and reset) each lane on a non-negative 0..MaxWindowMs ms axis
            // with 10 ms ticks, instead of ScottPlot's default +/- range, so the
            // stopped/initial view and a post-run reset read in ms from 0 rather
            // than showing negative placeholder ticks. Live data re-labels these
            // via ApplyTimeTicks once the sample rate and beat window are known.
            plot.Axes.SetLimitsX(0, MaxWindowMs);
            var initialTicks = new ScottPlot.TickGenerators.NumericManual();
            for (double posMs = 0.0; posMs <= MaxWindowMs + 1e-6; posMs += 10.0)
            {
                initialTicks.AddMajor(posMs, ((int)posMs).ToString());
            }
            plot.Axes.Bottom.TickGenerator = initialTicks;
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
    /// Re-fits all four lanes to the live beat-locked window (the Reset View /
    /// right-click "Auto Scale" action).
    /// </summary>
    public void ResetView()
    {
        _followLive = true;
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
    /// Sets the display decimation budget (the density slider). Higher = more
    /// points (smoother, more render cost); clamped to [MinDisplayBudget, the
    /// producer's full budget]. Takes effect on the next published frame.
    /// </summary>
    public void SetDisplayBudget(int budget)
    {
        _displayBudget = Math.Clamp(budget, MinDisplayBudget, MultiFilterFrameProjector.FilterPointBudget);
    }

    /// <summary>
    /// Puts a time ruler on the lane's bottom axis in milliseconds measured from
    /// the window's left edge, so the leftmost tick reads 0 ms (X is absolute
    /// sample ticks, so 1 ms = sampleRate / 1000 ticks). The tick step is chosen
    /// from a 1 / 5 / 10 / 20 ms ladder by the visible span, so as the view
    /// auto-scales the ruler stays readable (and near the 100 ms max window it
    /// settles on the 10/20 ms steps). All lanes share the same positions for
    /// aligned gridlines. No-op before the rate is known.
    /// </summary>
    private void ApplyTimeTicks(int lane, double left, double right)
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        double samplesPerMs = _sampleRate / 1000.0;
        double spanMs = (right - left) / samplesPerMs;
        double stepMs = TickStepMs(spanMs);
        var ticks = new ScottPlot.TickGenerators.NumericManual();
        for (double posMs = 0.0; posMs <= spanMs + 1e-6; posMs += stepMs)
        {
            double position = left + posMs * samplesPerMs; // left edge = 0 ms
            ticks.AddMajor(position, ((int)Math.Round(posMs)).ToString());
        }

        _plots[lane].Plot.Axes.Bottom.TickGenerator = ticks;
    }

    /// <summary>
    /// The 1 / 5 / 10 / 20 ms tick step for a visible span (ms): finer when
    /// zoomed in, coarser toward the 100 ms max window. Shared by the tick ruler
    /// and the window-edge stabilizer so both agree on the step.
    /// </summary>
    private static double TickStepMs(double spanMs) =>
        spanMs <= 10.0 ? 1.0
        : spanMs <= 50.0 ? 5.0
        : spanMs <= 100.0 ? 10.0
        : 20.0;

    /// <summary>
    /// Holds the window's right edge to a stable ms-tick boundary so the
    /// rightmost tick label does not flicker as the beat period jitters by a
    /// fraction of a ms. The largest tick that fits the raw span is sticky (with
    /// a half-step deadband on the way down), and the edge is extended a hair
    /// past it so it never clips. Returns the right edge in absolute sample
    /// ticks; falls back to the raw edge before the rate is known.
    /// </summary>
    private double StableWindowRight(double left, double rawRight)
    {
        if (_sampleRate <= 0)
        {
            return rawRight;
        }

        double samplesPerMs = _sampleRate / 1000.0;
        double rawSpanMs = (rawRight - left) / samplesPerMs;
        double step = TickStepMs(rawSpanMs);
        double candidate = Math.Floor(rawSpanMs / step + 1e-9) * step; // largest tick that fits
        // Grow at once when the span clears a new tick; shrink only after it
        // falls a half-step below the held tick, so jitter at the boundary does
        // not toggle the label.
        if (candidate > _stickyMaxTickMs || rawSpanMs < _stickyMaxTickMs - 0.5 * step)
        {
            _stickyMaxTickMs = candidate;
        }

        // Extend a hair past the sticky tick so it sits just inside the axis
        // (never on the clipped edge); never shrink below the actual data.
        double rightMs = Math.Max(rawSpanMs, _stickyMaxTickMs + 0.05 * step);
        return left + rightMs * samplesPerMs;
    }

    /// <summary>
    /// Computes the beat-locked window: one beat period wide, centered on the most
    /// recent A (beat) onset that fits (with half a period of retained data on each
    /// side), so a single beat shows centered and holds there (the spectrogram
    /// Last Beat behavior). The period is the average A-to-A onset spacing over the
    /// recent beats. X is in absolute sample ticks, so onset tick = onset seconds *
    /// sampleRate. Returns false if no locked beat fits, so the caller keeps the
    /// Filter Scope hidden until the detector can supply a beat window.
    /// </summary>
    private bool TryBeatWindow(AnalysisFrame frame, double oldest, double newest, out double min, out double max)
    {
        min = 0.0;
        max = 0.0;
        IReadOnlyList<BeatSegment>? segments = frame.BeatSegments?.Segments;
        if (segments == null || segments.Count < 2 || _sampleRate <= 0)
        {
            return false;
        }

        double OnsetTick(int i) =>
            (segments[i].StartTimeS + segments[i].AOffsetMs / 1000.0) * _sampleRate;

        double period = (OnsetTick(segments.Count - 1) - OnsetTick(0)) / (segments.Count - 1);
        if (period <= 0.0)
        {
            return false;
        }

        // Show a fraction of the period so the beat impulse spreads horizontally,
        // with the onset left-aligned (a small pre-roll) so its decay fills out to
        // the right. Capped at MaxWindowMs so a slow watch still tops out at 100 ms.
        double width = Math.Min(period * BeatWindowFraction, MaxWindowMs / 1000.0 * _sampleRate);
        double preroll = width * BeatPrerollFraction;
        double minOnset = oldest + preroll;          // left edge stays within the data
        double maxOnset = newest - (width - preroll); // right edge stays within the data
        if (maxOnset < minOnset)
        {
            return false;
        }

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            double onset = OnsetTick(i);
            if (onset >= minOnset && onset <= maxOnset)
            {
                min = onset - preroll;
                max = onset - preroll + width;
                return true;
            }
        }

        return false;
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
    /// Sets a lane's Y view to 1.2x its running peak amplitude. The peak only
    /// grows within a run (running max), so the axis never shrinks when a quieter
    /// beat passes — the waveform never clips and the amplitude scale stays steady
    /// instead of fluctuating. Mirrored lanes are symmetric about 0; the
    /// one-sided lane (F3) is pinned to [0, top].
    /// </summary>
    private void ApplyY(int lane)
    {
        double peak = Math.Max(LanePeak(lane), _lanePeakMax[lane]);
        _lanePeakMax[lane] = peak;
        if (peak <= 0.0)
        {
            return;
        }

        double half = YHeadroom * peak;
        if (MultiFilterScopeLanes.All[lane].Mirrored)
        {
            _plots[lane].Plot.Axes.SetLimitsY(-half, half);
        }
        else
        {
            _plots[lane].Plot.Axes.SetLimitsY(0.0, half);
        }
    }

    /// <summary>
    /// Clamps a lane's X view to the owner's live drawn-data extent
    /// (<see cref="_dataMinX"/>..<see cref="_dataMaxX"/>): the view never shows an
    /// empty margin beyond the data, and a span at or past the full extent snaps
    /// to it. The extent rolls with the window, so it is read live on every
    /// render, keeping the beat-locked X view inside the retained data.
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
                // No data yet (initial / stopped / acquiring): hold the 0..100 ms
                // axis so the leftmost reads 0, overriding any empty-plot
                // autoscale that would otherwise show a negative range.
                _xAxis.Range.Min = 0.0;
                _xAxis.Range.Max = MaxWindowMs;
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
        if (!frame.BeatSynced)
        {
            ClearDisplay();
            return;
        }

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
                SeriesDataReducer.TryReplaceSeriesDataPeak(
                    laneSeries, _x[i], _y[i], _displayBudget);
            if (updated[i])
            {
                _lastSeries[i] = laneSeries;
                UpdateMirror(i);
            }
        }

        bool hasBeatWindow = false;
        bool windowChanged = false;
        if (_x[0].Count > 0)
        {
            if (_followLive)
            {
                double newest = _x[0][^1];
                double oldest = _x[0][0];
                hasBeatWindow = TryBeatWindow(frame, oldest, newest, out double beatMin, out double beatMax);
                if (hasBeatWindow)
                {
                    double candidateMin = beatMin;
                    double candidateMax = StableWindowRight(beatMin, beatMax);
                    // Only re-commit the window when an edge moves past the
                    // deadband, so per-frame beat-period jitter no longer re-scales
                    // the axis (which trembled the ticks). A real beat re-lock jumps
                    // by ~one period and clears the band, so the lock still advances.
                    double deadband = WindowDeadbandMs * Math.Max(_sampleRate / 1000.0, 1.0);
                    if (_dataMaxX <= _dataMinX
                        || Math.Abs(candidateMin - _dataMinX) > deadband
                        || Math.Abs(candidateMax - _dataMaxX) > deadband)
                    {
                        _dataMinX = candidateMin;
                        _dataMaxX = candidateMax;
                        windowChanged = true;
                    }
                }
            }

            _hasDataExtent = true;
        }
        else
        {
            _hasDataExtent = false;
        }

        if (!hasBeatWindow)
        {
            ClearDisplay();
            return;
        }

        for (int i = 0; i < _plots.Length; i++)
        {
            bool cursorMoved = UpdateReviewCursor(i, context);

            if (updated[i] && _x[i].Count > 0)
            {
                // Y always auto-fits to 1.2x the running peak so the waveform never
                // clips and the scale stays steady — independent of X interaction.
                // The X window only follows the beat-tracked extent while following
                // live, and only when it actually moved (the deadband), so the axis
                // limits and ms ruler are left untouched on the in-between frames.
                if (_followLive && windowChanged)
                {
                    _plots[i].Plot.Axes.SetLimitsX(_dataMinX, _dataMaxX);
                }

                ApplyY(i);
            }

            if (updated[i] || cursorMoved)
            {
                // Re-lay the ms ruler only when the window moved. Rebuilding the
                // tick generator every frame (even with identical positions) was
                // what trembled the bottom axis; with the deadband holding the
                // window steady, the ticks now stay put between beat re-locks.
                if (windowChanged)
                {
                    AxisLimits limits = _plots[i].Plot.Axes.GetLimits();
                    ApplyTimeTicks(i, limits.Left, limits.Right);
                }

                _plots[i].Refresh();
            }
        }
    }

    private void ClearDisplay()
    {
        bool changed = _hasDataExtent;
        _hasDataExtent = false;
        _dataMinX = 0.0;
        _dataMaxX = 0.0;
        _stickyMaxTickMs = 0.0;
        Array.Clear(_lanePeakMax);
        for (int i = 0; i < _plots.Length; i++)
        {
            changed |= _x[i].Count > 0 || _y[i].Count > 0 || _yMirror[i].Count > 0 || _lastSeries[i] != null;
            _x[i].Clear();
            _y[i].Clear();
            _yMirror[i].Clear();
            _lastSeries[i] = null;
        }

        if (changed)
        {
            RefreshAll();
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
        PlotThemeHelper.ApplyCompactAxisPanels(plot);
    }

    private void RefreshAll()
    {
        for (int i = 0; i < _plots.Length; i++)
        {
            _plots[i].Refresh();
        }
    }

}
