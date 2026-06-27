using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Scope Sweep: the Core-folded sweep window envelope (sweep.trace) on a single
/// plot, oscilloscope style — a stable pattern at nominal rate, drift when the
/// watch runs fast or slow. The scatter refills its point lists in place from
/// the frame's replace snapshot; the reference line of current readings under
/// the plot re-renders only when the cumulative history snapshot version
/// changes. The review cursor maps the scrubbed stream time onto its phase
/// within the sweep window.
/// </summary>
internal sealed class ScopeSweepRenderer
{
    private readonly AvaPlot _sweepPlot;
    private readonly TextBlock[] _referenceValueTexts;

    private readonly List<double> _sweepX = new();
    private readonly List<double> _sweepY = new();
    // Wrap-around copy of the last XPreRollMs worth of sweep bins, shifted
    // left by windowMs so the pre-onset region appears at negative X values.
    private readonly List<double> _preRollX = new();
    private readonly List<double> _preRollY = new();
    private readonly List<double> _aLegendX = new();
    private readonly List<double> _aLegendY = new();
    private readonly List<double> _cLegendX = new();
    private readonly List<double> _cLegendY = new();

    private Scatter? _sweepScatter;
    private Scatter? _preRollScatter;
    private Scatter? _aLegendScatter;
    private Scatter? _cLegendScatter;
    private ReviewCursorLayer? _reviewCursor;

    private const float MarkerLegendLineWidth = 2.0f;
    private const int MaxSweepMultiple = 3;
    /// <summary>Pre-roll margin shown to the left of the A onset (ms).</summary>
    private const double XPreRollMs = -10.0;
    private const double MarkerLineLeftGuardMs = 0.75;
    /// <summary>Extra Y kept above the fitted signal when the locked view grows,
    /// so the ordinary beat-to-beat variation (and the startup ramp) settles
    /// instead of re-zooming the view on every frame.</summary>
    private const double YGrowHeadroomFraction = 0.15;
    private readonly VerticalLine?[] _aTicMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly VerticalLine?[] _aTocMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly VerticalLine?[] _cTicMarkers = new VerticalLine?[MaxSweepMultiple];
    private readonly VerticalLine?[] _cTocMarkers = new VerticalLine?[MaxSweepMultiple];

    // Identity gate on the projector's shared-instance pattern: between
    // publish-floor rebuilds (and on every paused-scrub re-route) frames
    // re-attach the same immutable GraphSeriesFrame, and a rebuild always
    // allocates a new one — so reference equality is a correct change
    // detector and skips the redundant copy/autoscale/refresh.
    private GraphSeriesFrame? _lastSweepSeries;
    // Tic-phase offset (ms) published with the last sweep series; used to
    // map the review cursor and beat markers onto the tic-aligned bin domain.
    private double _ticPhaseOffsetMs;
    // Last known window size (ms); a change re-arms live fitting so the
    // view snaps to [0, new window] when 1x/2x/3x is pressed.
    private double _lastWindowMs;

    // Last-seen beat-segment snapshot version; lets RenderFrame update marker
    // positions when a new beat arrives even if the sweep bins haven't
    // published yet (sweep publishes on a slower interval than beats arrive).
    private ulong _lastSegmentVersion;

    private PlotThemePalette _theme = PlotThemePalette.Current;
    private ulong _lastReadoutVersion;
    private ulong _lastReadoutSegmentVersion;
    private bool _followLive = true;

    // Latched once the detector first locks onto the tick/tock beat. The sweep
    // draws nothing before this, so the pre-lock fold (fallback window, unaligned
    // phase) never paints a meaningless trace. Latched, not live, so a later sync
    // dropout keeps the accumulated pattern instead of blanking it (the sweep is
    // designed to survive dropouts).
    private bool _beatSynced;

    // Canonical "initial output" view. Y ([_viewYMin, _viewYMax]) is captured on
    // the first fit and on Reset/AutoScale only; live follow and cycle changes
    // keep it and re-fit just the X window ([XPreRollMs, _viewWindowMs]), so
    // toggling the multiple never rescales Y. SweepViewRule enforces these on
    // every render, so a mouse zoom acts on X only (Y is held) and zoom-out
    // cannot pass the full window. False until the first fit has data.
    private double _viewWindowMs;
    private double _viewYMin;
    private double _viewYMax;
    private bool _hasView;

    public ScopeSweepRenderer(AvaPlot sweepPlot, TextBlock[] referenceValueTexts)
    {
        _sweepPlot = sweepPlot;
        _referenceValueTexts = referenceValueTexts;

        _sweepPlot.PointerWheelChanged += (_, _) => _followLive = false;
        _sweepPlot.PointerPressed += (_, _) => _followLive = false;
    }

    public void ApplyTheme(PlotThemePalette theme)
    {
        _theme = theme;
        ApplyPlotTheme(_sweepPlot.Plot);
        ApplySeriesTheme();
        _sweepPlot.Refresh();
    }

    public void CreateGraphs()
    {
        _lastReadoutVersion = 0;
        _lastReadoutSegmentVersion = 0;
        _lastSegmentVersion = 0;
        _followLive = true;
        _beatSynced = false;
        _ticPhaseOffsetMs = 0.0;
        _lastWindowMs = 0.0;
        _viewWindowMs = 0.0;
        _viewYMin = 0.0;
        _viewYMax = 0.0;
        _hasView = false;
        string[] initialValues = ScopeSweepReadout.Values(null);
        for (int i = 0; i < _referenceValueTexts.Length && i < initialValues.Length; i++)
        {
            _referenceValueTexts[i].Text = initialValues[i];
        }

        Plot sweep = _sweepPlot.Plot;
        sweep.Clear();
        _sweepX.Clear();
        _sweepY.Clear();
        _preRollX.Clear();
        _preRollY.Clear();
        _aLegendX.Clear();
        _aLegendY.Clear();
        _cLegendX.Clear();
        _cLegendY.Clear();
        _lastSweepSeries = null;
        ApplyPlotTheme(sweep);
        sweep.YLabel("Signal Level");
        sweep.XLabel("Sweep (ms)");
        _sweepScatter = sweep.Add.Scatter(_sweepX, _sweepY);
        _sweepScatter.LineWidth = 1;
        _sweepScatter.MarkerStyle.IsVisible = false;
        _preRollScatter = sweep.Add.Scatter(_preRollX, _preRollY);
        _preRollScatter.LineWidth = 1;
        _preRollScatter.MarkerStyle.IsVisible = false;
        _aLegendScatter = sweep.Add.Scatter(_aLegendX, _aLegendY);
        _aLegendScatter.LineWidth = MarkerLegendLineWidth;
        _aLegendScatter.LinePattern = GraphLinePatterns.VerticalGuide;
        _aLegendScatter.MarkerStyle.IsVisible = false;
        _aLegendScatter.LegendText = "A";
        _cLegendScatter = sweep.Add.Scatter(_cLegendX, _cLegendY);
        _cLegendScatter.LineWidth = MarkerLegendLineWidth;
        _cLegendScatter.LinePattern = GraphLinePatterns.VerticalGuide;
        _cLegendScatter.MarkerStyle.IsVisible = false;
        _cLegendScatter.LegendText = "C";
        sweep.ShowLegend(Alignment.LowerRight);

        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            _aTicMarkers[k] = AddMarkerLine(sweep, GraphLinePatterns.VerticalGuide);
            _aTocMarkers[k] = AddMarkerLine(sweep, GraphLinePatterns.VerticalGuide);
            _cTicMarkers[k] = AddMarkerLine(sweep, GraphLinePatterns.VerticalGuide);
            _cTocMarkers[k] = AddMarkerLine(sweep, GraphLinePatterns.VerticalGuide);
        }

        _reviewCursor = AddCursor(sweep);
        sweep.Axes.Rules.Clear();
        sweep.Axes.Rules.Add(new SweepViewRule(this, sweep.Axes.Bottom, sweep.Axes.Left));

        ApplySeriesTheme();
        _sweepPlot.Refresh();
    }

    public void Reset()
    {
        CreateGraphs();
    }

    /// <summary>
    /// Re-arms live auto-fitting and re-captures the canonical view via
    /// <see cref="FitCanonicalView"/> (the one place, with the first fit, that
    /// re-autoscales Y), so AutoScale reproduces the initial graph output.
    /// </summary>
    public void ResetView()
    {
        _followLive = true;
        if (!FitCanonicalView())
        {
            _sweepPlot.Plot.Axes.AutoScale();
        }

        _sweepPlot.Refresh();
    }

    /// <summary>
    /// Captures the canonical "initial output" view from the current data and
    /// applies it: X pinned to [XPreRollMs, window] and Y to the data autoscale
    /// fit. <see cref="SweepViewRule"/> then holds Y locked and bounds X to this
    /// window, so zoom is X-only and cannot pass the full window, and Reset
    /// restores exactly this. Returns false (view unchanged) before any visible
    /// sweep signal exists.
    /// </summary>
    private bool FitCanonicalView()
    {
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
        if (windowMs <= 0 || !HasVisibleSweepSignal())
        {
            return false;
        }

        // Let ScottPlot fit Y to the data, capture that range as the locked
        // canonical Y, then pin X to the pre-roll..window span.
        _sweepPlot.Plot.Axes.AutoScale();
        AxisLimits limits = _sweepPlot.Plot.Axes.GetLimits();
        _viewWindowMs = windowMs;
        _viewYMin = limits.Bottom;
        _viewYMax = limits.Top;
        _hasView = true;
        _sweepPlot.Plot.Axes.SetLimits(XPreRollMs, windowMs, _viewYMin, _viewYMax);
        return true;
    }

    /// <summary>
    /// Re-fits the X window to the current data ([XPreRollMs, window]) and grows
    /// the captured Y only when the current signal would clip it, never shrinking
    /// it. The startup signal ramps up from a faint first beat, and a
    /// sweep-multiple toggle briefly clears the bins; shrinking Y back to that
    /// partial data would lock a too-small range (the trace then clips off the
    /// top until Reset View) or make Y jump on a multiple change. Each grow
    /// overshoots by <see cref="YGrowHeadroomFraction"/> so the ordinary
    /// beat-to-beat variation does not re-zoom the view every frame; the view
    /// settles once it covers the steady-state signal. Returns false before any
    /// sweep data exists.
    /// </summary>
    private bool FitXGrowY()
    {
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
        if (windowMs <= 0)
        {
            return false;
        }

        _viewWindowMs = windowMs;

        // Read the data's autoscale extent, then grow (never shrink) the locked
        // Y, but only when the signal would otherwise clip — and overshoot by a
        // headroom margin so a slightly louder beat does not re-zoom next frame.
        _sweepPlot.Plot.Axes.AutoScale();
        AxisLimits fit = _sweepPlot.Plot.Axes.GetLimits();
        if (fit.Bottom < _viewYMin)
        {
            _viewYMin = fit.Bottom;
        }

        if (fit.Top > _viewYMax)
        {
            _viewYMax = fit.Top + (fit.Top - _viewYMin) * YGrowHeadroomFraction;
        }

        _sweepPlot.Plot.Axes.SetLimits(XPreRollMs, windowMs, _viewYMin, _viewYMax);
        return true;
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // Draw nothing until the detector locks onto the tick/tock beat: before
        // the lock the fold uses a fallback window and an unaligned phase, so it
        // would paint a meaningless, re-zooming trace. Once latched it stays on,
        // so a later dropout keeps the trace rather than blanking it.
        _beatSynced |= frame.BeatSynced;

        GraphSeriesFrame? sweepSeries = _beatSynced
            ? SeriesDataReducer.FindSeries(frame.ScopeSeries, AnalysisGraphSeries.SweepTrace)
            : null;
        bool dataUpdated = !ReferenceEquals(sweepSeries, _lastSweepSeries) &&
            SeriesDataReducer.TryReplaceSeriesData(sweepSeries, _sweepX, _sweepY, SweepFrameProjector.SweepBinBudget);
        if (dataUpdated)
        {
            _lastSweepSeries = sweepSeries;
            _ticPhaseOffsetMs = sweepSeries!.TicPhaseOffsetMs;

            // Re-arm live fitting when the window size changes.
            // so the X view snaps to the new window even if the user had panned.
            // Y is kept (FitXKeepY), so a multiple change does not rescale Y.
            double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
            if (Math.Abs(windowMs - _lastWindowMs) > 0.001)
            {
                _lastWindowMs = windowMs;
                _followLive = true;
            }

            UpdatePreRoll();
        }

        // Update markers whenever the sweep data refreshes OR a new beat segment
        // arrives — segments publish at the beat rate while sweep data publishes
        // on a slower interval, so gating on dataUpdated alone leaves markers
        // stale for several beats between sweep publishes.
        ulong segVersion = frame.BeatSegments?.Version ?? 0;
        bool segmentsChanged = segVersion != _lastSegmentVersion;
        if (segmentsChanged) _lastSegmentVersion = segVersion;

        if (dataUpdated || segmentsChanged)
        {
            UpdateSweepMarkerPositions(frame.BeatSegments, frame.MetricsHistory);
        }

        bool cursorMoved = UpdateReviewCursor(context.ReviewCursorTimeS);

        if (dataUpdated && _followLive)
        {
            // First fit captures the canonical Y (autoscale). After that, live
            // follow and cycle changes re-fit the X window and GROW the captured
            // Y to the current signal without ever shrinking it, so the startup
            // signal can ramp up into view (no clip until Reset View) while a
            // sweep-multiple toggle never rescales Y back down.
            if (_hasView)
            {
                FitXGrowY();
            }
            else
            {
                FitCanonicalView();
            }
        }

        UpdateReferenceLine(frame.MetricsHistory, frame.BeatSegments);

        if (dataUpdated || cursorMoved || segmentsChanged)
        {
            _sweepPlot.Refresh();
        }
    }

    private void UpdateReferenceLine(BeatMetricsHistorySnapshot? history, BeatSegmentsSnapshot? segments)
    {
        ulong hv = history?.Version ?? 0;
        ulong sv = segments?.Version ?? 0;
        if (hv == _lastReadoutVersion && sv == _lastReadoutSegmentVersion)
        {
            return;
        }

        _lastReadoutVersion = hv;
        _lastReadoutSegmentVersion = sv;
        string[] values = ScopeSweepReadout.Values(history, segments);
        for (int i = 0; i < _referenceValueTexts.Length && i < values.Length; i++)
        {
            _referenceValueTexts[i].Text = values[i];
        }
    }

    /// <summary>
    /// Places A and C vertical markers at each beat phase in
    /// the sweep window. In 1x mode one set of markers is placed; in 2x/3x mode
    /// additional copies are placed at each subsequent beat-period repetition so
    /// the markers remain visible across the full multi-period window.
    /// </summary>
    private void UpdateSweepMarkerPositions(BeatSegmentsSnapshot? snapshot,
        BeatMetricsHistorySnapshot? history)
    {
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);

        // Keep last-known positions during a sweep window retune or startup
        // accumulation — hiding here would cause a visible blink every time
        // the 1x/2x/3x selector is pressed. After a retune, all bins are
        // cleared to zero; skip updates until real signal returns so the
        // markers hold their pre-switch positions rather than jumping to the
        // phase-unaligned (offset = 0) coordinates the projector emits
        // immediately after the window reset.
        if (snapshot == null || snapshot.Segments.Count == 0 || windowMs <= 0 || !HasVisibleSweepSignal())
        {
            return;
        }

        // Beat period: one full oscillation (tic→tic), which equals two BPH
        // half-beats. Using 3600000/bph (the half-oscillation) would give twice
        // as many repetitions as needed and double-paint every marker.
        double beatPeriodMs = history is { Bph: > 0 }
            ? 7200000.0 / history.Bph
            : windowMs;
        int nReps = Math.Clamp((int)Math.Round(windowMs / beatPeriodMs), 1, MaxSweepMultiple);

        BeatSegment? latestTic = null, latestToc = null;
        foreach (BeatSegment seg in snapshot.Segments)
        {
            if (seg.IsTic) latestTic = seg;
            else latestToc = seg;
        }

        double? aTicPhase = PhaseMs(latestTic, isC: false, windowMs);
        // The projector aligns the tic onset to near bin 0. Floating-point
        // residuals can push the modulo to just below windowMs instead of
        // just above 0; folding such a value back makes the marker appear at
        // the correct leftmost position (or in the pre-roll region near x=0)
        // rather than at the right edge of the sweep.
        if (aTicPhase is double ap && ap > windowMs * 0.75)
        {
            aTicPhase = ap - windowMs; // small negative → pre-roll range
        }
        double? aTocPhase = PhaseMs(latestToc, isC: false, windowMs);
        double? cTicPhase = PhaseMs(
            latestTic is { CPeakValid: true } ? latestTic : null, isC: true, windowMs);
        double? cTocPhase = PhaseMs(
            latestToc is { CPeakValid: true } ? latestToc : null, isC: true, windowMs);

        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            bool active = k < nReps;
            SetMarkerLine(_aTicMarkers[k], active ? RepeatPhase(aTicPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_aTocMarkers[k], active ? RepeatPhase(aTocPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_cTicMarkers[k], active ? RepeatPhase(cTicPhase, k, beatPeriodMs, windowMs) : null);
            SetMarkerLine(_cTocMarkers[k], active ? RepeatPhase(cTocPhase, k, beatPeriodMs, windowMs) : null);
        }
    }

    /// <summary>
    /// Returns the X position of the k-th repetition of a marker phase within
    /// the sweep window (phase shifted by k beat periods). Returns null when the
    /// base phase is absent or the repeated position falls outside the window.
    /// </summary>
    private static double? RepeatPhase(double? phase, int k, double beatPeriodMs, double windowMs)
    {
        if (phase is not double p) return null;
        double x = p + k * beatPeriodMs;
        return x < windowMs ? x : null;
    }

    private double? PhaseMs(BeatSegment? seg, bool isC, double windowMs)
    {
        if (seg == null) return null;
        double offsetMs = isC ? seg.CPeakOffsetMs : seg.AOffsetMs;
        double phase = (seg.StartTimeS * 1000.0 + offsetMs + _ticPhaseOffsetMs) % windowMs;
        return phase < 0.0 ? phase + windowMs : phase;
    }

    private static void SetMarkerLine(VerticalLine? line, double? x)
    {
        if (line == null) return;
        if (x is double value && value >= MarkerLineLeftGuardMs)
        {
            line.IsVisible = true;
            line.X = value;
            return;
        }

        line.IsVisible = false;
    }

    private bool HasVisibleSweepSignal()
    {
        return _sweepY.Count > 0 && _sweepY.Max() > 0.0;
    }

    /// <summary>Review-cursor contract: a vertical marker at the scrub time's sweep phase.</summary>
    private bool UpdateReviewCursor(double? reviewCursorTimeS)
    {
        if (_reviewCursor == null)
        {
            return false;
        }

        double? phaseMs = ScopeSweepReadout.CursorPhaseMs(
            reviewCursorTimeS, ScopeSweepReadout.WindowMs(_sweepX), _ticPhaseOffsetMs);
        return _reviewCursor.Update(phaseMs);
    }

    private ReviewCursorLayer AddCursor(Plot plot)
    {
        var cursor = new ReviewCursorLayer(plot);
        cursor.ApplyTheme(_theme);
        return cursor;
    }

    private static VerticalLine AddMarkerLine(Plot plot, LinePattern pattern)
    {
        VerticalLine line = plot.Add.VerticalLine(0.0);
        line.LineWidth = 1;
        line.LinePattern = pattern;
        line.IsVisible = false;
        line.EnableAutoscale = false;
        return line;
    }

    /// <summary>
    /// Fills <see cref="_preRollX"/>/<see cref="_preRollY"/> with the last
    /// |<see cref="XPreRollMs"/>| worth of sweep bins shifted left by the window
    /// width so the periodic signal wraps into the negative-X pre-onset region.
    /// The pre-roll scatter uses the same data source as the main sweep scatter
    /// but spans [XPreRollMs, 0) instead of [0, windowMs).
    /// </summary>
    private void UpdatePreRoll()
    {
        _preRollX.Clear();
        _preRollY.Clear();
        double windowMs = ScopeSweepReadout.WindowMs(_sweepX);
        if (windowMs <= 0 || _sweepX.Count != _sweepY.Count) return;

        double preRollStartMs = windowMs + XPreRollMs; // windowMs - 10
        for (int i = 0; i < _sweepX.Count; i++)
        {
            if (_sweepX[i] > preRollStartMs)
            {
                _preRollX.Add(_sweepX[i] - windowMs); // shift into [-10, 0)
                _preRollY.Add(_sweepY[i]);
            }
        }
    }

    private void ApplySeriesTheme()
    {
        if (_sweepScatter != null)
        {
            _sweepScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        if (_preRollScatter != null)
        {
            _preRollScatter.LineColor = Color.FromARGB(_theme.TraceWave);
        }

        Color aColor = Color.FromARGB(_theme.TraceTick);
        Color cColor = Color.FromARGB(_theme.TraceTock);
        if (_aLegendScatter != null)
        {
            _aLegendScatter.LineColor = aColor;
        }

        if (_cLegendScatter != null)
        {
            _cLegendScatter.LineColor = cColor;
        }

        for (int k = 0; k < MaxSweepMultiple; k++)
        {
            if (_aTicMarkers[k] != null) _aTicMarkers[k]!.LineColor     = aColor;
            if (_aTocMarkers[k] != null) _aTocMarkers[k]!.LineColor     = aColor;
            if (_cTicMarkers[k] != null) _cTicMarkers[k]!.LineColor     = cColor;
            if (_cTocMarkers[k] != null) _cTocMarkers[k]!.LineColor     = cColor;
        }

        _reviewCursor?.ApplyTheme(_theme);
    }

    private void ApplyPlotTheme(Plot plot)
    {
        PlotThemeHelper.Apply(plot, _theme);
        PlotThemeHelper.ApplyCompactAxisPanels(plot);
        plot.Legend.BackgroundColor = Color.FromARGB(_theme.ScopeBg);
        plot.Legend.FontColor = Color.FromARGB(_theme.TextPrimary);
        plot.Legend.OutlineColor = Color.FromARGB(_theme.ScopeGrid);
    }

    /// <summary>
    /// Holds the sweep view at its captured canonical extent: Y is locked to
    /// [<see cref="_viewYMin"/>, <see cref="_viewYMax"/>] so a mouse zoom/pan
    /// never moves it (zoom acts on X only), and X is floored at
    /// <see cref="XPreRollMs"/> and capped at the window so zoom-out cannot pass
    /// the initial output. No-op until the first fit captures a view.
    /// </summary>
    private sealed class SweepViewRule : IAxisRule
    {
        private readonly ScopeSweepRenderer _owner;
        private readonly IXAxis _xAxis;
        private readonly IYAxis _yAxis;

        public SweepViewRule(ScopeSweepRenderer owner, IXAxis xAxis, IYAxis yAxis)
        {
            _owner = owner;
            _xAxis = xAxis;
            _yAxis = yAxis;
        }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            if (!_owner._hasView)
            {
                return;
            }

            // Lock Y to the captured fit: zoom/pan acts on X only.
            _yAxis.Range.Min = _owner._viewYMin;
            _yAxis.Range.Max = _owner._viewYMax;

            // Bound X to [XPreRollMs, window]: a zoom-out at or past the full
            // span snaps to it; otherwise the window is shifted back inside the
            // bound. Mirrors the MultiFilterScope/RateScope X-bounds rules.
            double min = XPreRollMs;
            double max = _owner._viewWindowMs;
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

}
