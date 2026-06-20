using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Metrics;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Cumulative per-beat history: bounded series accumulation, stream-time snapshot
/// throttling with instance sharing in between, and the frame-projection contract.
/// </summary>
public sealed class BeatMetricsHistoryTests
{
    private static WatchMetricsUpdate BeatUpdate(
        ulong beat, double timeS, double rateSPerDay, double beatErrorMs = 0.0, bool beatErrorValid = true, int bph = 28800)
    {
        var update = new WatchMetricsUpdate();
        update.SetBeatTimingSample(new BeatTimingSample(
            beat, timeS, IsTic: (beat & 1) == 1, RateErrorMs: 0.0,
            RateValid: true, RateSPerDay: rateSPerDay,
            BeatErrorValid: beatErrorValid, BeatErrorSignedMs: beatErrorMs,
            Bph: bph));
        update.SetDerivedMeasures(new DerivedTimingMeasures(true, 0.1, true, 0.2, true, 0.3));
        return update;
    }

    private static WatchMetricsUpdate AmplitudeUpdate(double timeS, double pairDeg)
    {
        var update = new WatchMetricsUpdate();
        update.SetAmplitudeSample(new AmplitudeSample(
            timeS, InstantValid: true, InstantDeg: pairDeg,
            PairAverageUpdated: true, PairAverageDeg: pairDeg));
        return update;
    }

    [Fact]
    public void SnapshotCarriesSeriesDerivedAndCurrentReadings()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0, beatErrorMs: 0.4));
        history.Record(AmplitudeUpdate(0.130, pairDeg: 280.0));

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();
        Assert.NotNull(snapshot);

        Assert.Equal(new[] { 0.125 }, snapshot!.Rate.X);
        Assert.Equal(new[] { 5.0 }, snapshot.Rate.Y);
        Assert.Equal(new[] { 280.0 }, snapshot.Amplitude.Y);
        Assert.Equal(new[] { 0.4 }, snapshot.BeatError.Y);

        Assert.True(snapshot.RateValid);
        Assert.Equal(5.0, snapshot.RateSPerDay);
        Assert.Equal(28800, snapshot.Bph);
        Assert.True(snapshot.AmplitudeValid);
        Assert.Equal(280.0, snapshot.AmplitudeDeg);
        Assert.True(snapshot.BeatErrorValid);
        Assert.Equal(0.4, snapshot.BeatErrorSignedMs);

        Assert.Equal(0.1, snapshot.Derived.DiffTicTacMs);
        Assert.Equal(0.130, snapshot.LatestTimeS);
    }

    [Fact]
    public void InvalidSampleClearsCurrentValidityWithoutDroppingHistory()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0, beatErrorMs: 0.4));
        BeatMetricsHistorySnapshot? valid = history.CurrentSnapshot();
        Assert.True(valid!.RateValid);
        Assert.True(valid.BeatErrorValid);

        // A detection gap delivers a sample the per-beat engine declares invalid
        // (>0.15 s later so a fresh snapshot is built). The current readout must
        // stop advertising the previous value as valid, but the already-plotted
        // series points must remain.
        var invalid = new WatchMetricsUpdate();
        invalid.SetBeatTimingSample(new BeatTimingSample(
            2, 0.750, IsTic: false, RateErrorMs: 0.0,
            RateValid: false, RateSPerDay: 0.0,
            BeatErrorValid: false, BeatErrorSignedMs: 0.0,
            Bph: 28800));
        history.Record(invalid);

        BeatMetricsHistorySnapshot? after = history.CurrentSnapshot();
        Assert.NotSame(valid, after);
        Assert.False(after!.RateValid);
        Assert.False(after.BeatErrorValid);
        Assert.Equal(new[] { 5.0 }, after.Rate.Y);
        Assert.Equal(new[] { 0.4 }, after.BeatError.Y);
    }

    [Fact]
    public void SnapshotIsSharedUntilThrottleElapses()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));

        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        // New data inside the 0.15 s stream-time window: same shared instance.
        history.Record(BeatUpdate(2, 0.250, 6.0));
        Assert.Same(first, history.CurrentSnapshot());

        // Once stream time advances past the throttle, the snapshot is rebuilt
        // and includes everything recorded meanwhile.
        history.Record(BeatUpdate(3, 0.750, 7.0));
        BeatMetricsHistorySnapshot? rebuilt = history.CurrentSnapshot();
        Assert.NotSame(first, rebuilt);
        Assert.Equal(3, rebuilt!.Rate.Y.Count);
        Assert.True(rebuilt.Version > first!.Version);
    }

    [Fact]
    public void SlowWatchPublishesPerBeatPeriodNotTheRateCap()
    {
        // 18000 bph = 0.20 s/beat, slower than the 24000-bph cap (0.15 s). A point
        // 0.15 s out (the cap window) is still inside this watch's beat period, so
        // it is coalesced; only a full beat period later does the snapshot rebuild.
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.0, 5.0, bph: 18000));
        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        history.Record(BeatUpdate(2, 0.15, 6.0, bph: 18000)); // 0.15 < 0.20 period
        Assert.Same(first, history.CurrentSnapshot());

        history.Record(BeatUpdate(3, 0.30, 7.0, bph: 18000)); // past the 0.20 period
        BeatMetricsHistorySnapshot? rebuilt = history.CurrentSnapshot();
        Assert.NotSame(first, rebuilt);
        Assert.Equal(3, rebuilt!.Rate.Y.Count);
    }

    [Fact]
    public void FastWatchCoalescesToTheRateCap()
    {
        // 36000 bph = 0.10 s/beat, faster than the 24000-bph cap (0.15 s).
        // Consecutive beats 0.10 s apart fall inside the cap window and coalesce;
        // the snapshot only rebuilds once stream time passes the 0.15 s cap.
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.0, 5.0, bph: 36000));
        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        history.Record(BeatUpdate(2, 0.10, 6.0, bph: 36000)); // 0.10 < 0.15 cap
        Assert.Same(first, history.CurrentSnapshot());

        history.Record(BeatUpdate(3, 0.20, 7.0, bph: 36000)); // past the 0.15 cap
        Assert.NotSame(first, history.CurrentSnapshot());
    }

    [Fact]
    public void ForcedSnapshotBypassesThrottleForFinalFrame()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));
        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        // New data inside the 0.15 s window is throttled (shared instance)...
        history.Record(BeatUpdate(2, 0.250, 6.0));
        Assert.Same(first, history.CurrentSnapshot());

        // ...but the end-of-run flush forces a rebuild so the last beats are not
        // dropped from the final frame / measurement CSV.
        BeatMetricsHistorySnapshot? forced = history.CurrentSnapshot(force: true);
        Assert.NotSame(first, forced);
        Assert.Equal(2, forced!.Rate.Y.Count);
        Assert.True(forced.Version > first!.Version);
    }

    [Fact]
    public void SnapshotIsNullBeforeFirstBeatAndAfterReset()
    {
        var history = new BeatMetricsHistory();
        Assert.Null(history.CurrentSnapshot());

        history.Record(BeatUpdate(1, 0.125, 5.0));
        Assert.NotNull(history.CurrentSnapshot());

        history.Reset();
        Assert.Null(history.CurrentSnapshot());
    }

    [Fact]
    public void SnapshotCarriesRunningStatsForRateAndAmplitude()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 4.0));
        history.Record(BeatUpdate(2, 0.250, rateSPerDay: 8.0));
        history.Record(AmplitudeUpdate(0.300, pairDeg: 280.0));
        history.Record(AmplitudeUpdate(0.500, pairDeg: 290.0));

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();

        StatsSummary rate = snapshot!.RateStats;
        Assert.True(rate.Valid);
        Assert.Equal(4.0, rate.Min);
        Assert.Equal(8.0, rate.Max);
        Assert.Equal(6.0, rate.Mean, 12);
        Assert.Equal(2.0, rate.Sigma, 12); // population sigma of {4, 8}
        Assert.Equal(2, rate.Count);

        StatsSummary amplitude = snapshot.AmplitudeStats;
        Assert.True(amplitude.Valid);
        Assert.Equal(280.0, amplitude.Min);
        Assert.Equal(290.0, amplitude.Max);
        Assert.Equal(285.0, amplitude.Mean, 12);
        Assert.Equal(5.0, amplitude.Sigma, 12);
        Assert.Equal(2, amplitude.Count);
    }

    [Fact]
    public void VarioStatsAndElapsedRestartOnPositionChangeButSeriesAndAggregatesStay()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 4.0));
        history.Record(BeatUpdate(2, 0.250, rateSPerDay: 8.0));
        BeatMetricsHistorySnapshot? before = history.CurrentSnapshot();
        Assert.Equal(2, before!.RateStats.Count);
        Assert.Equal(0.250, before.StatsElapsedS, 9);

        // Turn the watch to a new position, then record one beat there.
        history.SetActivePosition(WatchPosition.P6H);
        history.Record(BeatUpdate(3, 5.250, rateSPerDay: -3.0));

        BeatMetricsHistorySnapshot? after = history.CurrentSnapshot();
        // Vario stats now cover only the new position...
        Assert.Equal(1, after!.RateStats.Count);
        Assert.Equal(-3.0, after.RateStats.Min);
        Assert.Equal(-3.0, after.RateStats.Max);
        // ...and the elapsed clock restarts from the position change (5.250 − 0.250).
        Assert.Equal(5.0, after.StatsElapsedS, 9);
        // The live series (Trace/Long-Term) and per-position aggregates are untouched.
        Assert.Equal(3, after.Rate.Y.Count);
        Assert.Equal(2, after.Positions.Count);
    }

    [Fact]
    public void AmplitudeValidityClearsOnPositionChange()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0));
        history.Record(AmplitudeUpdate(0.130, pairDeg: 280.0));
        Assert.True(history.CurrentSnapshot()!.AmplitudeValid);

        // Turning to a new position must not carry the prior position's amplitude
        // forward as a current valid reading (unlike rate/beat-error, amplitude
        // validity never self-heals on the next beat, so it would otherwise stay
        // true and contaminate the graded measurement CSV with the old position).
        history.SetActivePosition(WatchPosition.P6H);
        BeatMetricsHistorySnapshot? afterTurn = history.CurrentSnapshot();
        Assert.False(afterTurn!.AmplitudeValid);

        // A fresh pair at the new position restores validity with the new value.
        history.Record(AmplitudeUpdate(5.200, pairDeg: 300.0));
        BeatMetricsHistorySnapshot? afterPair = history.CurrentSnapshot();
        Assert.True(afterPair!.AmplitudeValid);
        Assert.Equal(300.0, afterPair.AmplitudeDeg);
    }

    [Fact]
    public void RunningStatsAreInvalidWithoutSamplesAndClearOnReset()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0));

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();
        Assert.True(snapshot!.RateStats.Valid);
        Assert.False(snapshot.AmplitudeStats.Valid); // no amplitude pair recorded yet

        history.Reset();
        history.Record(BeatUpdate(2, 0.250, rateSPerDay: 7.0));

        StatsSummary restarted = history.CurrentSnapshot()!.RateStats;
        Assert.Equal(1, restarted.Count); // pre-reset sample is gone
        Assert.Equal(7.0, restarted.Min);
        Assert.Equal(7.0, restarted.Max);
    }

    [Fact]
    public void RunningStatsStayExactWhileSeriesDecimate()
    {
        // Capacity 4 forces the plotted series to coarsen; the stats must keep
        // counting every recorded beat regardless.
        var history = new BeatMetricsHistory(seriesCapacity: 4);
        for (int i = 0; i < 100; i++)
        {
            history.Record(BeatUpdate((ulong)(i + 1), i * 0.125, rateSPerDay: i));
        }

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();
        Assert.InRange(snapshot!.Rate.Y.Count, 1, 4);
        Assert.Equal(100, snapshot.RateStats.Count);
        Assert.Equal(0.0, snapshot.RateStats.Min);
        Assert.Equal(99.0, snapshot.RateStats.Max);
        Assert.Equal(49.5, snapshot.RateStats.Mean, 12);
    }

    [Fact]
    public void SeriesStayBoundedOverLongRuns()
    {
        var history = new BeatMetricsHistory(seriesCapacity: 64);
        for (int i = 0; i < 10_000; i++)
        {
            history.Record(BeatUpdate((ulong)(i + 1), i * 0.125, 5.0));
        }

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();
        Assert.InRange(snapshot!.Rate.Y.Count, 1, 64);
    }

    [Fact]
    public void PositionChangeAndSequenceResetPublishWithoutWaitingForBeats()
    {
        // The stream-time throttle is keyed to beat time, which does not
        // advance while the watch is off the mic; state changes must publish
        // anyway or the position UI stays stale until sync resumes.
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));
        BeatMetricsHistorySnapshot? beforeChange = history.CurrentSnapshot();

        history.SetActivePosition(WatchPosition.P9H); // no beats since
        BeatMetricsHistorySnapshot? restamped = history.CurrentSnapshot();
        Assert.NotSame(beforeChange, restamped);
        Assert.Equal(WatchPosition.P9H, restamped!.ActivePosition);

        history.ResetPositionAggregates(); // still no beats
        BeatMetricsHistorySnapshot? cleared = history.CurrentSnapshot();
        Assert.NotSame(restamped, cleared);
        Assert.Empty(cleared!.Positions);
    }

    [Fact]
    public void ReApplyingTheSamePositionDoesNotBypassTheThrottle()
    {
        // The projector re-applies the volatile position knob on EVERY pass;
        // only the unchanged-position early return keeps that from raising the
        // publish-immediately flag each pass and silently voiding the 0.15 s
        // throttle (every version-gated renderer would re-render per frame).
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));
        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        history.SetActivePosition(WatchPosition.CH); // already the active position
        history.Record(BeatUpdate(2, 0.250, 6.0));   // inside the throttle window

        Assert.Same(first, history.CurrentSnapshot());
    }

    [Fact]
    public void ProjectorPassesWithAnUnchangedPositionKnobShareTheSnapshot()
    {
        var projector = new BeatMetricsFrameProjector();
        var result = new DetectorResultSnapshot(
            TgSyncStatus.Synced, 28800, 0.125, Array.Empty<TgEvent>(),
            Array.Empty<float>(), 0, 0UL, false, false, false, 0f, 0f, 0f, 0f);
        projector.SetActivePosition(WatchPosition.P12H);
        projector.Project(new DetectorMetricsBlockUpdate(result, new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 6000.0, BeatUpdate(1, 0.125, 5.0)),
        }));
        var firstFrame = new AnalysisFrame();
        projector.AppendSnapshot(firstFrame);

        // A second pass with the knob unchanged and a beat inside the 0.15 s
        // throttle window: the per-pass knob re-apply must not bypass it.
        projector.Project(new DetectorMetricsBlockUpdate(result, new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 12000.0, BeatUpdate(2, 0.250, 6.0)),
        }));
        var secondFrame = new AnalysisFrame();
        projector.AppendSnapshot(secondFrame);

        Assert.Same(firstFrame.MetricsHistory, secondFrame.MetricsHistory);
    }

    [Fact]
    public void SnapshotStampsTheActivePositionAndDefaultsToDialUp()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));

        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();
        Assert.Equal(WatchPosition.CH, first!.ActivePosition);

        // Stream time must clear the 0.15 s throttle before the new stamp shows.
        history.SetActivePosition(WatchPosition.P3H);
        history.Record(BeatUpdate(2, 0.750, 6.0));

        BeatMetricsHistorySnapshot? restamped = history.CurrentSnapshot();
        Assert.NotSame(first, restamped);
        Assert.Equal(WatchPosition.P3H, restamped!.ActivePosition);
    }

    [Fact]
    public void PositionAggregatesTagEachMeasurementWithTheActivePosition()
    {
        var history = new BeatMetricsHistory();

        // Two rated beats and one amplitude pair while dial up...
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 4.0, beatErrorMs: 0.2));
        history.Record(BeatUpdate(2, 0.250, rateSPerDay: 8.0, beatErrorMs: 0.4));
        history.Record(AmplitudeUpdate(0.300, pairDeg: 280.0));

        // ...then one rated beat while crown left.
        history.SetActivePosition(WatchPosition.P6H);
        history.Record(BeatUpdate(3, 0.750, rateSPerDay: -2.0, beatErrorMs: 0.6));

        IReadOnlyList<PositionSummary> positions = history.CurrentSnapshot()!.Positions;

        // Only measured positions appear, in WatchPositions.All order.
        Assert.Equal(2, positions.Count);
        Assert.Equal(WatchPosition.CH, positions[0].Position);
        Assert.Equal(WatchPosition.P6H, positions[1].Position);

        PositionSummary dialUp = positions[0];
        Assert.Equal(2, dialUp.Rate.Count);
        Assert.Equal(6.0, dialUp.Rate.Mean, 12);
        Assert.Equal(4.0, dialUp.Rate.Min);
        Assert.Equal(8.0, dialUp.Rate.Max);
        Assert.Equal(2.0, dialUp.Rate.Sigma, 12); // population sigma of {4, 8}
        Assert.Equal(1, dialUp.Amplitude.Count);
        Assert.Equal(280.0, dialUp.Amplitude.Mean);
        Assert.Equal(2, dialUp.BeatError.Count);
        Assert.Equal(0.3, dialUp.BeatError.Mean, 12);

        PositionSummary crownLeft = positions[1];
        Assert.Equal(1, crownLeft.Rate.Count);
        Assert.Equal(-2.0, crownLeft.Rate.Mean);
        Assert.False(crownLeft.Amplitude.Valid); // no pair recorded in 6H yet
        Assert.Equal(0.6, crownLeft.BeatError.Mean);
    }

    [Fact]
    public void PositionChangesOpenAtTheFirstBeatThenRecordEachTurn()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0));

        // The start entry is stamped at the first beat (0.125), not elapsed 0, so
        // the Long-Term graph's start label lines up with the first plotted point.
        IReadOnlyList<PositionChange> opening = history.CurrentSnapshot()!.PositionChanges;
        PositionChange start = Assert.Single(opening);
        Assert.Equal(0.125, start.TimeS);
        Assert.Equal(WatchPosition.CH, start.Position);

        // Turning the watch after data records a change at the turn-command time
        // (the latest beat time then); stream time may jump afterwards (watch off
        // the mic while repositioning).
        history.Record(BeatUpdate(2, 0.250, rateSPerDay: 5.0));
        history.SetActivePosition(WatchPosition.P6H);
        history.Record(BeatUpdate(3, 5.250, rateSPerDay: -3.0));

        IReadOnlyList<PositionChange> changes = history.CurrentSnapshot()!.PositionChanges;
        Assert.Equal(2, changes.Count);
        Assert.Equal(new PositionChange(0.125, WatchPosition.CH), changes[0]);
        Assert.Equal(new PositionChange(0.250, WatchPosition.P6H), changes[1]);
    }

    [Fact]
    public void PositionChangesFoldPreBeatTurnsIntoTheStartAndClearOnReset()
    {
        // Positions chosen before the first beat only set where the run starts;
        // the start entry records the final pre-beat choice at the first beat.
        var history = new BeatMetricsHistory();
        history.SetActivePosition(WatchPosition.P9H);
        history.SetActivePosition(WatchPosition.P12H);
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0));

        Assert.Equal(
            new PositionChange(0.125, WatchPosition.P12H),
            Assert.Single(history.CurrentSnapshot()!.PositionChanges));

        history.Reset();
        history.Record(BeatUpdate(2, 0.300, rateSPerDay: 5.0));
        // The active position survives reset, so the fresh run opens on it at its
        // own first beat time.
        Assert.Equal(
            new PositionChange(0.300, WatchPosition.P12H),
            Assert.Single(history.CurrentSnapshot()!.PositionChanges));
    }

    [Fact]
    public void PositionListStaysBoundedAtTheWatchPositionsCatalog()
    {
        var history = new BeatMetricsHistory();
        double timeS = 0.125;
        foreach (WatchPosition position in WatchPositions.All)
        {
            history.SetActivePosition(position);
            history.Record(BeatUpdate((ulong)(timeS * 8), timeS, rateSPerDay: 1.0));
            timeS += 1.0;
        }

        IReadOnlyList<PositionSummary> positions = history.CurrentSnapshot()!.Positions;
        Assert.Equal(WatchPositions.Count, positions.Count);
        Assert.Equal(WatchPositions.All, positions.Select(summary => summary.Position));
    }

    [Fact]
    public void ResetPositionAggregatesClearsSummariesButKeepsSeriesAndPosition()
    {
        var history = new BeatMetricsHistory();
        history.SetActivePosition(WatchPosition.P9H);
        history.Record(BeatUpdate(1, 0.125, rateSPerDay: 5.0));
        Assert.Single(history.CurrentSnapshot()!.Positions);

        history.ResetPositionAggregates();
        history.Record(BeatUpdate(2, 0.750, rateSPerDay: 6.0));

        BeatMetricsHistorySnapshot? snapshot = history.CurrentSnapshot();
        // The sequence restart drops the aggregates; the run's series, overall
        // stats and the active position stamp keep going.
        PositionSummary restarted = Assert.Single(snapshot!.Positions);
        Assert.Equal(1, restarted.Rate.Count);
        Assert.Equal(2, snapshot.Rate.Y.Count);
        Assert.Equal(2, snapshot.RateStats.Count);
        Assert.Equal(WatchPosition.P9H, snapshot.ActivePosition);
    }

    [Fact]
    public void ProjectorAppliesTheVolatilePositionKnobOnProject()
    {
        var projector = new BeatMetricsFrameProjector();
        var result = new DetectorResultSnapshot(
            TgSyncStatus.Synced, 28800, 0.125, Array.Empty<TgEvent>(),
            Array.Empty<float>(), 0, 0UL, false, false, false, 0f, 0f, 0f, 0f);

        // Requested from "another thread" before the pass: the beat recorded by
        // the pass must land in the requested position's aggregate.
        projector.SetActivePosition(WatchPosition.P12H);
        projector.Project(new DetectorMetricsBlockUpdate(result, new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 6000.0, BeatUpdate(1, 0.125, 5.0)),
        }));

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame);

        Assert.Equal(WatchPosition.P12H, frame.MetricsHistory!.ActivePosition);
        PositionSummary summary = Assert.Single(frame.MetricsHistory.Positions);
        Assert.Equal(WatchPosition.P12H, summary.Position);
        Assert.Equal(1, summary.Rate.Count);
    }

    [Fact]
    public void ProjectorAppliesTheVolatileAggregateResetOnProject()
    {
        var projector = new BeatMetricsFrameProjector();
        var result = new DetectorResultSnapshot(
            TgSyncStatus.Synced, 28800, 0.125, Array.Empty<TgEvent>(),
            Array.Empty<float>(), 0, 0UL, false, false, false, 0f, 0f, 0f, 0f);

        projector.Project(new DetectorMetricsBlockUpdate(result, new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 6000.0, BeatUpdate(1, 0.125, 5.0)),
        }));

        // Requested from "another thread" between passes: the clear must apply
        // before the next pass records, so only the post-reset beat survives.
        projector.ResetPositionAggregates();
        projector.Project(new DetectorMetricsBlockUpdate(result, new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 6000.0, BeatUpdate(2, 0.750, 6.0)),
        }));

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame);

        PositionSummary summary = Assert.Single(frame.MetricsHistory!.Positions);
        Assert.Equal(1, summary.Rate.Count); // pre-reset beat is gone
        Assert.Equal(6.0, summary.Rate.Mean);
        Assert.Equal(2, frame.MetricsHistory.Rate.Y.Count); // run series keeps going
    }

    [Fact]
    public void ProjectorAttachesSnapshotToFrame()
    {
        var projector = new BeatMetricsFrameProjector();
        var result = new DetectorResultSnapshot(
            TgSyncStatus.Synced, 28800, 0.125, Array.Empty<TgEvent>(),
            Array.Empty<float>(), 0, 0UL, false, false, false, 0f, 0f, 0f, 0f);
        var events = new List<DetectedEventUpdate>
        {
            new(new TgEvent { Type = TgEventType.A }, 6000.0, BeatUpdate(1, 0.125, 5.0)),
        };

        projector.Project(new DetectorMetricsBlockUpdate(result, Array.Empty<DetectedEventUpdate>(), events));

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame);

        Assert.NotNull(frame.MetricsHistory);
        Assert.Equal(new[] { 5.0 }, frame.MetricsHistory!.Rate.Y);
    }
}
