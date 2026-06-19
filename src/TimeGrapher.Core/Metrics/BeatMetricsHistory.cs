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

    private bool _dirty;

    // State changes (active position, sequence reset) bypass the stream-time
    // throttle: it is keyed to _latestTimeS, which only advances with synced
    // beats, so a position picked while no beats arrive (watch off the mic)
    // would otherwise never publish and the position UI would stay stale.
    private bool _publishImmediately;
    private ulong _version;
    private BeatMetricsHistorySnapshot? _snapshot;
    private double _lastSnapshotTimeS;

    public BeatMetricsHistory(int seriesCapacity = DefaultSeriesCapacity)
    {
        _rate = new DecimatingSeries(seriesCapacity);
        _amplitude = new DecimatingSeries(seriesCapacity);
        _beatError = new DecimatingSeries(seriesCapacity);
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

        _activePosition = position;
        // Vario reports stability for the current position only, so its running
        // statistics and elapsed clock restart when the watch turns to a new
        // position. The live series and per-position aggregates are untouched.
        _rateStats.Reset();
        _amplitudeStats.Reset();
        _statsStartTimeS = _latestTimeS;
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
                ActiveAggregate().Rate.Add(sample.RateSPerDay);
                _rateSPerDay = sample.RateSPerDay;
            }

            // Track the current validity from every sample: after a detection gap
            // or re-lock warmup the per-beat engine declares the sample invalid,
            // and the snapshot must not keep republishing the previous reading as
            // a current valid value (e.g. into the measurement CSV).
            _rateValid = sample.RateValid;

            if (sample.BeatErrorValid)
            {
                SeedStartPositionIfNeeded(sample.TimeS);
                _beatError.Add(sample.TimeS, sample.BeatErrorSignedMs);
                ActiveAggregate().BeatError.Add(sample.BeatErrorSignedMs);
                _beatErrorSignedMs = sample.BeatErrorSignedMs;
            }

            _beatErrorValid = sample.BeatErrorValid;

            _dirty = true;
        }

        if (update.AmplitudeSampleUpdated && update.AmplitudeSample.PairAverageUpdated)
        {
            AmplitudeSample sample = update.AmplitudeSample;
            SeedStartPositionIfNeeded(sample.TimeS);
            _amplitude.Add(sample.TimeS, sample.PairAverageDeg);
            _amplitudeStats.Add(sample.PairAverageDeg);
            ActiveAggregate().Amplitude.Add(sample.PairAverageDeg);
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
        _dirty = false;
        _publishImmediately = false;
        _snapshot = null;
        _lastSnapshotTimeS = 0.0;
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
        _dirty = true;
        _publishImmediately = true;
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
        _snapshot = new BeatMetricsHistorySnapshot
        {
            Version = _version,
            Rate = BuildSeries(_rate),
            Amplitude = BuildSeries(_amplitude),
            BeatError = BuildSeries(_beatError),
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
    }

    private PositionAggregate ActiveAggregate()
    {
        return _positionAggregates[(int)_activePosition] ??= new PositionAggregate();
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

    private IReadOnlyList<PositionSummary> BuildPositionSummaries()
    {
        int measured = 0;
        foreach (PositionAggregate? aggregate in _positionAggregates)
        {
            if (aggregate != null)
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
            if (_positionAggregates[(int)position] is { } aggregate)
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
