using TimeGrapher.Core.Shared;

namespace TimeGrapher.Core.Metrics;

/// <summary>
/// Accumulates the numeric per-beat samples emitted by <see cref="WatchMetrics"/>
/// into bounded decimating series (rate, amplitude, beat error) plus the latest
/// derived measures and instantaneous readings, and publishes them as immutable
/// <see cref="BeatMetricsHistorySnapshot"/>s. Beat-data rebuilds happen at most
/// once per <see cref="SnapshotMinIntervalS"/> of stream time; a user state
/// change (active position, sequence reset) bypasses that throttle once, since
/// stream time stands still while no synced beats arrive. Unchanged or
/// in-between requests return the same shared instance, so per-frame cost
/// stays flat.
/// </summary>
public sealed class BeatMetricsHistory
{
    public const int DefaultSeriesCapacity = 4096;
    public const double SnapshotMinIntervalS = 0.5;

    private readonly DecimatingSeries _rate;
    private readonly DecimatingSeries _amplitude;
    private readonly DecimatingSeries _beatError;

    // Per-position store sizing. The per-position rate/amplitude series use the
    // same capacity as the global series so a position's Trace looks identical to
    // the global trace for the same beats; the rate-error rings reproduce the
    // WatchMetrics ring, so they wrap on the same scale and slot count.
    private readonly int _seriesCapacity;
    private readonly double _rateErrorYScale;
    private readonly int _rateErrorRingCapacity;

    // Vario stability statistics: fed per beat (before decimation), so min/max/
    // mean/sigma stay exact however coarse the plotted series become. They cover a
    // single watch position and restart on a position change (see SetActivePosition),
    // because mixing positions would inflate the spread and misreport stability.
    private readonly RunningStats _rateStats = new();
    private readonly RunningStats _amplitudeStats = new();

    // Per-position aggregates, indexed by WatchPosition ordinal. A slot is
    // created on the first measurement tagged with that position, so storage is
    // bounded by the WatchPositions.Count-entry catalog (10) regardless of run length.
    private readonly PositionAggregate?[] _positionAggregates =
        new PositionAggregate?[WatchPositions.Count];
    private WatchPosition _activePosition = WatchPosition.CH;

    // Chronological position turns since the run started: the first entry is the
    // starting position, stamped at the first plotted point (not TimeS 0; see
    // SeedStartPositionIfNeeded), each later entry the elapsed time of a turn.
    // Bounded only by how often the user turns the watch (manual, seconds apart),
    // so its growth is negligible against the per-beat path.
    private readonly List<PositionChange> _positionChanges = new();

    private DerivedTimingMeasures _derived;
    private bool _rateValid;
    private double _rateSPerDay;
    private int _bph;
    private bool _amplitudeValid;
    private double _amplitudeDeg;
    private bool _beatErrorValid;
    private double _beatErrorSignedMs;
    private double _latestTimeS;

    // Baseline for the Vario stats' elapsed clock, re-anchored on a position change
    // so the displayed elapsed time counts from when the current position started.
    private double _statsStartTimeS;

    // Start of the active position's current measurement segment. The per-position
    // series plot a re-based elapsed time (the position's own accumulated measuring
    // time, excluding time spent at other positions) so a position's Trace stays
    // contiguous across revisits: x = aggregate.AccumulatedElapsedS + (timeS - this).
    // The boundary is the latest recorded beat time at the switch, not the UI-turn
    // instant (no stream timestamp travels with the position knob) — the same
    // approximation the Vario _statsStartTimeS boundary already uses, so the offset
    // is at most one inter-beat interval. The run's first segment keeps this at 0,
    // so the starting position's first point sits at its first beat's stream time
    // (matching the prior global Trace behavior), not at a re-based 0.
    private double _activeSegmentStartTimeS;

    // Active-position rate-error trace cache (raw series), published on every frame
    // (not the snapshot's throttle) so the Rate Scope / Beat Error traces stay as
    // live as they were when sourced from the global WatchMetrics ring. Rebuilt only
    // when a beat changed the active ring or the active position changed; the same
    // instance is returned in between so the projector can reuse one wrapper.
    private MetricsHistorySeries _activeRateTicSeries = MetricsHistorySeries.Empty;
    private MetricsHistorySeries _activeRateTocSeries = MetricsHistorySeries.Empty;
    private bool _rateErrorDirty = true;

    private bool _dirty;

    // State changes (active position, sequence reset) bypass the stream-time
    // throttle: it is keyed to _latestTimeS, which only advances with synced
    // beats, so a position picked while no beats arrive (watch off the mic)
    // would otherwise never publish and the position UI would stay stale.
    private bool _publishImmediately;
    private ulong _version;
    private BeatMetricsHistorySnapshot? _snapshot;
    private double _lastSnapshotTimeS;

    public BeatMetricsHistory(
        int seriesCapacity = DefaultSeriesCapacity,
        double rateErrorYScale = 10.0,
        int rateErrorRingCapacity = 250)
    {
        _rate = new DecimatingSeries(seriesCapacity);
        _amplitude = new DecimatingSeries(seriesCapacity);
        _beatError = new DecimatingSeries(seriesCapacity);
        _seriesCapacity = seriesCapacity;
        _rateErrorYScale = rateErrorYScale;
        _rateErrorRingCapacity = rateErrorRingCapacity;
    }

    /// <summary>
    /// Tags subsequent measurements with the given test position. Analysis-thread
    /// only (the UI request travels through the projector's volatile knob); a
    /// change re-stamps the next snapshot.
    /// </summary>
    public void SetActivePosition(WatchPosition position)
    {
        if (_activePosition == position)
        {
            return;
        }

        // Freeze the outgoing position's elapsed clock so its per-position series
        // resumes (not restarts) when the watch returns to it: the time spent at
        // other positions is excluded, keeping each position's elapsed axis
        // contiguous across revisits. Read before reassigning _activePosition.
        PositionAggregate? outgoing = _positionAggregates[(int)_activePosition];
        if (outgoing != null)
        {
            outgoing.AccumulatedElapsedS += Math.Max(0.0, _latestTimeS - _activeSegmentStartTimeS);
        }

        _activePosition = position;
        _activeSegmentStartTimeS = _latestTimeS;
        // Vario reports stability for the current position only, so its running
        // statistics and elapsed clock restart when the watch turns to a new
        // position. The global series and per-position aggregates are untouched;
        // the active rate-error trace must rebuild to show the new position's ring.
        _rateStats.Reset();
        _amplitudeStats.Reset();
        _statsStartTimeS = _latestTimeS;
        _rateErrorDirty = true;
        // Record the turn on the change timeline only once the run has data: the
        // start entry is seeded at the first plotted point, not the first beat (so
        // it lines up with where the graph begins drawing), and a turn before that
        // point just changes which position the start will record.
        if (_positionChanges.Count > 0)
        {
            AppendPositionChange(_latestTimeS, position);
        }

        _dirty = true;
        _publishImmediately = true;
    }

    public void Record(WatchMetricsUpdate update)
    {
        if (update.BeatTimingSampleUpdated)
        {
            BeatTimingSample sample = update.BeatTimingSample;
            _latestTimeS = sample.TimeS;
            _bph = sample.Bph;
            PositionAggregate active = ActiveAggregate();

            if (sample.RateValid)
            {
                // Seed the start marker at the first point that actually enters a
                // series, not at the first beat: rate needs two beats, so the
                // first beat is rate-invalid and the plotted line begins a beat
                // or two later. Seeding here keeps the start label on the first
                // drawn point.
                SeedStartPositionIfNeeded(sample.TimeS);
                _rate.Add(sample.TimeS, sample.RateSPerDay);
                _rateStats.Add(sample.RateSPerDay);
                active.Rate.Add(sample.RateSPerDay);
                active.RateSeries.Add(ActiveElapsed(sample.TimeS), sample.RateSPerDay);
                _rateValid = true;
                _rateSPerDay = sample.RateSPerDay;
            }

            // The tic/toc rate-error trace is recorded for every synced beat, not
            // only RateValid ones (the s/d rate needs two beats per phase), so the
            // per-position trace matches the global WatchMetrics ring point-for-point.
            // This "every synced beat" set holds because WatchMetrics emits a
            // BeatTimingSample only for synced beats — the invariant is enforced
            // upstream, not re-checked here. RateErrorMs is the same un-wrapped
            // instant the global ring wraps, so re-wrapping it on the same scale
            // reproduces that trace, per position. We intentionally do NOT seed the
            // position-change start marker on this path: the marker aligns with the
            // first plotted point of the global series (Long-Term), not the ring.
            double wrapped = WatchMetrics.WrapIntoRange(sample.RateErrorMs, -_rateErrorYScale, _rateErrorYScale);
            (sample.IsTic ? active.RateTicRing : active.RateTocRing).AddOrOverwrite(wrapped);
            _rateErrorDirty = true;

            if (sample.BeatErrorValid)
            {
                SeedStartPositionIfNeeded(sample.TimeS);
                _beatError.Add(sample.TimeS, sample.BeatErrorSignedMs);
                active.BeatError.Add(sample.BeatErrorSignedMs);
                _beatErrorValid = true;
                _beatErrorSignedMs = sample.BeatErrorSignedMs;
            }

            _dirty = true;
        }

        if (update.AmplitudeSampleUpdated && update.AmplitudeSample.PairAverageUpdated)
        {
            AmplitudeSample sample = update.AmplitudeSample;
            SeedStartPositionIfNeeded(sample.TimeS);
            PositionAggregate active = ActiveAggregate();
            _amplitude.Add(sample.TimeS, sample.PairAverageDeg);
            _amplitudeStats.Add(sample.PairAverageDeg);
            active.Amplitude.Add(sample.PairAverageDeg);
            active.AmplitudeSeries.Add(ActiveElapsed(sample.TimeS), sample.PairAverageDeg);
            _amplitudeValid = true;
            _amplitudeDeg = sample.PairAverageDeg;
            _latestTimeS = Math.Max(_latestTimeS, sample.TimeS);
            _dirty = true;
        }

        if (update.DerivedMeasuresUpdated)
        {
            _derived = update.DerivedMeasures;
            _dirty = true;
        }
    }

    public void Reset()
    {
        _rate.Reset();
        _amplitude.Reset();
        _beatError.Reset();
        _rateStats.Reset();
        _amplitudeStats.Reset();
        // The active position is the watch's physical orientation, not run
        // data, so it survives the reset; only its accumulated stats clear.
        Array.Clear(_positionAggregates);
        _positionChanges.Clear();
        _derived = default;
        _rateValid = false;
        _bph = 0;
        _amplitudeValid = false;
        _beatErrorValid = false;
        _latestTimeS = 0.0;
        _statsStartTimeS = 0.0;
        _activeSegmentStartTimeS = 0.0;
        _dirty = false;
        _publishImmediately = false;
        _snapshot = null;
        _lastSnapshotTimeS = 0.0;
        _activeRateTicSeries = MetricsHistorySeries.Empty;
        _activeRateTocSeries = MetricsHistorySeries.Empty;
        _rateErrorDirty = true;
    }

    /// <summary>
    /// Clears only the per-position aggregates (live series and overall stats
    /// keep accumulating). The multi-position sequence flow restarts position
    /// statistics mid-run through this; analysis-thread only (the UI request
    /// travels through the projector's volatile knob, the SetActivePosition flow).
    /// </summary>
    public void ResetPositionAggregates()
    {
        Array.Clear(_positionAggregates);
        // The cleared active slot is recreated by the next beat with a fresh
        // elapsed clock, so re-anchor the segment start to now; otherwise the
        // recreated per-position series would start at a stale elapsed offset.
        _activeSegmentStartTimeS = _latestTimeS;
        _dirty = true;
        _publishImmediately = true;
        _rateErrorDirty = true;
    }

    /// <summary>
    /// Latest snapshot, rebuilt only when content changed and either the
    /// stream-time throttle elapsed or a state change requested an immediate
    /// publish (the first build is immediate). Null until the first beat -
    /// unless a state change precedes it, which publishes a position-only
    /// snapshot with empty series.
    /// </summary>
    public BeatMetricsHistorySnapshot? CurrentSnapshot()
    {
        if (!_dirty && _snapshot != null)
        {
            return _snapshot;
        }

        if (_snapshot != null &&
            !_publishImmediately &&
            _latestTimeS - _lastSnapshotTimeS < SnapshotMinIntervalS)
        {
            return _snapshot;
        }

        if (!_dirty)
        {
            return _snapshot;
        }

        _version++;
        PositionAggregate? activeAggregate = _positionAggregates[(int)_activePosition];
        _snapshot = new BeatMetricsHistorySnapshot
        {
            Version = _version,
            Rate = BuildSeries(_rate),
            Amplitude = BuildSeries(_amplitude),
            BeatError = BuildSeries(_beatError),
            // Current-position rate/amplitude over the position's re-based elapsed
            // time; the Trace tab's two graphs. Empty until the active position has
            // a measurement (so a never-measured position renders an empty plot).
            ActivePositionRate = activeAggregate is null ? MetricsHistorySeries.Empty : BuildSeries(activeAggregate.RateSeries),
            ActivePositionAmplitude = activeAggregate is null ? MetricsHistorySeries.Empty : BuildSeries(activeAggregate.AmplitudeSeries),
            Derived = _derived,
            RateValid = _rateValid,
            RateSPerDay = _rateSPerDay,
            Bph = _bph,
            AmplitudeValid = _amplitudeValid,
            AmplitudeDeg = _amplitudeDeg,
            BeatErrorValid = _beatErrorValid,
            BeatErrorSignedMs = _beatErrorSignedMs,
            LatestTimeS = _latestTimeS,
            StatsElapsedS = Math.Max(0.0, _latestTimeS - _statsStartTimeS),
            RateStats = Summarize(_rateStats),
            AmplitudeStats = Summarize(_amplitudeStats),
            ActivePosition = _activePosition,
            Positions = BuildPositionSummaries(),
            PositionChanges = _positionChanges.ToArray(),
        };
        _lastSnapshotTimeS = _latestTimeS;
        _dirty = false;
        _publishImmediately = false;
        return _snapshot;
    }

    private sealed class PositionAggregate
    {
        public readonly RunningStats Rate = new();
        public readonly RunningStats Amplitude = new();
        public readonly RunningStats BeatError = new();

        // Plottable per-position history: rate (s/d) and amplitude (deg) over the
        // position's re-based elapsed time (Trace), and the tic/toc rate-error
        // rings (ms vs ring slot) the Rate Scope / Beat Error traces draw.
        public readonly DecimatingSeries RateSeries;
        public readonly DecimatingSeries AmplitudeSeries;
        public readonly RateErrorRing RateTicRing;
        public readonly RateErrorRing RateTocRing;

        // Measuring time accrued at this position across prior visits, so a
        // revisit's series resumes after this offset (excluding time at others).
        public double AccumulatedElapsedS;

        public PositionAggregate(int seriesCapacity, int ringCapacity)
        {
            RateSeries = new DecimatingSeries(seriesCapacity);
            AmplitudeSeries = new DecimatingSeries(seriesCapacity);
            RateTicRing = new RateErrorRing(ringCapacity);
            RateTocRing = new RateErrorRing(ringCapacity);
        }
    }

    private PositionAggregate ActiveAggregate()
    {
        return _positionAggregates[(int)_activePosition] ??=
            new PositionAggregate(_seriesCapacity, _rateErrorRingCapacity);
    }

    /// <summary>Re-based elapsed time of the active position at the given stream time.</summary>
    private double ActiveElapsed(double timeS)
    {
        return ActiveAggregate().AccumulatedElapsedS + (timeS - _activeSegmentStartTimeS);
    }

    /// <summary>
    /// Active position's tic/toc rate-error traces (ms vs ring slot) for the Rate
    /// Scope rate-error pane and the Beat Error tab, as raw series. Always non-null:
    /// <see cref="MetricsHistorySeries.Empty"/> for a position with no points of
    /// that phase, so the consumer replaces-to-empty (clears the prior position's
    /// trace) on a switch to a never-measured position. Cached and shared across
    /// frames until a beat or a position change marks it dirty (the same instance
    /// is returned in between). <see cref="BeatMetricsFrameProjector"/> wraps these
    /// in RateTic/RateToc replace series and publishes them on every frame (not the
    /// snapshot throttle), so the traces stay as live as the global ring was.
    /// </summary>
    public void CurrentActiveRateError(out MetricsHistorySeries tic, out MetricsHistorySeries toc)
    {
        if (_rateErrorDirty)
        {
            PositionAggregate? active = _positionAggregates[(int)_activePosition];
            _activeRateTicSeries = active is null ? MetricsHistorySeries.Empty : BuildRing(active.RateTicRing);
            _activeRateTocSeries = active is null ? MetricsHistorySeries.Empty : BuildRing(active.RateTocRing);
            _rateErrorDirty = false;
        }

        tic = _activeRateTicSeries;
        toc = _activeRateTocSeries;
    }

    private static MetricsHistorySeries BuildRing(RateErrorRing ring)
    {
        if (ring.Count == 0)
        {
            return MetricsHistorySeries.Empty;
        }

        var x = new List<double>(ring.Count);
        var y = new List<double>(ring.Count);
        ring.SnapshotTo(x, y);
        return new MetricsHistorySeries { X = x, Y = y };
    }

    private void SeedStartPositionIfNeeded(double timeS)
    {
        // The run's starting position is stamped at the first point that enters a
        // plotted series, so the Long-Term graph's start label lines up with the
        // first drawn point. Turns taken before then have only updated
        // _activePosition, so the seed records wherever the watch actually started.
        if (_positionChanges.Count == 0)
        {
            _positionChanges.Add(new PositionChange(timeS, _activePosition));
        }
    }

    private void AppendPositionChange(double timeS, WatchPosition position)
    {
        // Collapse turns that land on the same instant (several positions picked
        // between two beats, while stream time stands still): the last choice at
        // that time is the one that took effect.
        if (_positionChanges.Count > 0 && _positionChanges[^1].TimeS >= timeS)
        {
            _positionChanges[^1] = new PositionChange(_positionChanges[^1].TimeS, position);
            return;
        }

        _positionChanges.Add(new PositionChange(timeS, position));
    }

    // A slot is allocated as soon as a beat feeds the per-position rate-error ring,
    // which happens a beat or two before the first valid rate/amplitude/beat-error
    // statistic exists. A position counts as "measured" (appears in the summary the
    // Positions / Long-Term tabs read) only once it has at least one such statistic,
    // so a ring-only slot does not surface an all-empty PositionSummary.
    private static bool HasStatSample(PositionAggregate aggregate) =>
        aggregate.Rate.Count > 0 || aggregate.Amplitude.Count > 0 || aggregate.BeatError.Count > 0;

    private IReadOnlyList<PositionSummary> BuildPositionSummaries()
    {
        int measured = 0;
        foreach (PositionAggregate? aggregate in _positionAggregates)
        {
            if (aggregate != null && HasStatSample(aggregate))
            {
                measured++;
            }
        }

        if (measured == 0)
        {
            return Array.Empty<PositionSummary>();
        }

        // Rebuilt with the snapshot (at most every SnapshotMinIntervalS), so
        // the allocation stays off the per-beat path and is bounded by WatchPositions.Count rows.
        var summaries = new List<PositionSummary>(measured);
        foreach (WatchPosition position in WatchPositions.All)
        {
            if (_positionAggregates[(int)position] is { } aggregate && HasStatSample(aggregate))
            {
                summaries.Add(new PositionSummary(
                    position,
                    Summarize(aggregate.Rate),
                    Summarize(aggregate.Amplitude),
                    Summarize(aggregate.BeatError)));
            }
        }

        return summaries;
    }

    private static StatsSummary Summarize(RunningStats stats) => new(
        stats.Count > 0, stats.Min, stats.Max, stats.Mean, stats.Sigma, stats.Count);

    private static MetricsHistorySeries BuildSeries(DecimatingSeries source)
    {
        if (source.Count == 0)
        {
            return MetricsHistorySeries.Empty;
        }

        var x = new List<double>(source.Count);
        var y = new List<double>(source.Count);
        var yMin = new List<double>(source.Count);
        var yMax = new List<double>(source.Count);
        source.SnapshotTo(x, y, yMin, yMax);
        return new MetricsHistorySeries { X = x, Y = y, YMin = yMin, YMax = yMax };
    }
}
