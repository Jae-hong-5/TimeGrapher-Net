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
        ulong beat, double timeS, double rateSPerDay, double beatErrorMs = 0.0, bool beatErrorValid = true)
    {
        var update = new WatchMetricsUpdate();
        update.SetBeatTimingSample(new BeatTimingSample(
            beat, timeS, IsTic: (beat & 1) == 1, RateErrorMs: 0.0,
            RateValid: true, RateSPerDay: rateSPerDay,
            BeatErrorValid: beatErrorValid, BeatErrorSignedMs: beatErrorMs));
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
        Assert.True(snapshot.AmplitudeValid);
        Assert.Equal(280.0, snapshot.AmplitudeDeg);
        Assert.True(snapshot.BeatErrorValid);
        Assert.Equal(0.4, snapshot.BeatErrorSignedMs);

        Assert.Equal(0.1, snapshot.Derived.DiffTicTacMs);
        Assert.Equal(0.130, snapshot.LatestTimeS);
    }

    [Fact]
    public void SnapshotIsSharedUntilThrottleElapses()
    {
        var history = new BeatMetricsHistory();
        history.Record(BeatUpdate(1, 0.125, 5.0));

        BeatMetricsHistorySnapshot? first = history.CurrentSnapshot();

        // New data inside the 0.5 s stream-time window: same shared instance.
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

        projector.Project(new DetectorMetricsBlockUpdate(result, events));

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame);

        Assert.NotNull(frame.MetricsHistory);
        Assert.Equal(new[] { 5.0 }, frame.MetricsHistory!.Rate.Y);
    }
}
