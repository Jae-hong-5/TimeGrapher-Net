using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Scope 2 lane averaging: phase-alternating lane assignment without tic/toc
/// labels, Σ on/off semantics, the 50+50 cycle freeze, per-lane mean peak
/// amplitude, and the capture integration that feeds lanes ahead of the full
/// segment windows.
/// </summary>
public sealed class BeatNoiseAveragerTests
{
    private static float[] Trace(float value, int peakIndex = -1, float peakValue = 0f)
    {
        var trace = new float[BeatNoiseAverager.LanePoints];
        Array.Fill(trace, value);
        if (peakIndex >= 0)
        {
            trace[peakIndex] = peakValue;
        }

        return trace;
    }

    [Fact]
    public void SigmaOn_AveragesTracesPerLaneAndTracksCounts()
    {
        var averager = new BeatNoiseAverager();
        averager.SetSigmaEnabled(true);

        averager.Add(firstLane: true, Trace(0.2f));
        averager.Add(firstLane: false, Trace(0.5f));
        averager.Add(firstLane: true, Trace(0.4f));

        BeatNoiseAverageSnapshot snapshot = averager.Snapshot();
        Assert.True(snapshot.SigmaEnabled);
        Assert.False(snapshot.Frozen);
        Assert.Equal(2, snapshot.Lane1Count);
        Assert.Equal(1, snapshot.Lane2Count);
        Assert.Equal(0.3f, snapshot.Lane1[0], 6);
        Assert.Equal(0.5f, snapshot.Lane2[0], 6);
        Assert.Equal(BeatNoiseAverager.MsPerPoint, snapshot.MsPerPoint);
    }

    [Fact]
    public void SigmaOff_LaneHoldsOnlyItsNewestTrace()
    {
        var averager = new BeatNoiseAverager();

        averager.Add(firstLane: true, Trace(0.2f));
        averager.Add(firstLane: true, Trace(0.6f));

        BeatNoiseAverageSnapshot snapshot = averager.Snapshot();
        Assert.False(snapshot.SigmaEnabled);
        Assert.Equal(1, snapshot.Lane1Count);
        Assert.Equal(0.6f, snapshot.Lane1[0], 6);
        Assert.False(snapshot.Frozen);
    }

    [Fact]
    public void Cycle_FreezesAfterFiftyIntervalsPerLane()
    {
        var averager = new BeatNoiseAverager();
        averager.SetSigmaEnabled(true);

        for (int i = 0; i < BeatNoiseAverager.IntervalsPerLane; i++)
        {
            Assert.True(averager.Add(firstLane: true, Trace(0.2f)));
        }

        // One lane full: not frozen yet, but its further beats are ignored.
        Assert.False(averager.Frozen);
        Assert.False(averager.Add(firstLane: true, Trace(0.9f)));

        for (int i = 0; i < BeatNoiseAverager.IntervalsPerLane; i++)
        {
            Assert.True(averager.Add(firstLane: false, Trace(0.4f)));
        }

        BeatNoiseAverageSnapshot snapshot = averager.Snapshot();
        Assert.True(snapshot.Frozen);
        Assert.Equal(BeatNoiseAverager.IntervalsPerLane, snapshot.Lane1Count);
        Assert.Equal(BeatNoiseAverager.IntervalsPerLane, snapshot.Lane2Count);
        // The ignored over-cycle trace did not bleed into the frozen average.
        Assert.Equal(0.2f, snapshot.Lane1[0], 6);
        Assert.False(averager.Add(firstLane: false, Trace(0.9f)));
    }

    [Fact]
    public void SigmaToggle_ResetsBothLanes()
    {
        var averager = new BeatNoiseAverager();
        averager.SetSigmaEnabled(true);
        averager.Add(firstLane: true, Trace(0.2f));
        averager.Add(firstLane: false, Trace(0.4f));

        Assert.True(averager.SetSigmaEnabled(false));
        BeatNoiseAverageSnapshot snapshot = averager.Snapshot();
        Assert.Equal(0, snapshot.Lane1Count);
        Assert.Equal(0, snapshot.Lane2Count);
        Assert.Empty(snapshot.Lane1);
        Assert.Empty(snapshot.Lane2);

        // No change: no reset.
        Assert.False(averager.SetSigmaEnabled(false));
    }

    [Fact]
    public void MeanPeak_IsTheMeanOfPerIntervalPeaks()
    {
        var averager = new BeatNoiseAverager();
        averager.SetSigmaEnabled(true);

        averager.Add(firstLane: true, Trace(0.1f, peakIndex: 10, peakValue: 0.8f));
        averager.Add(firstLane: true, Trace(0.1f, peakIndex: 20, peakValue: 0.4f));

        BeatNoiseAverageSnapshot snapshot = averager.Snapshot();
        Assert.Equal(0.6, snapshot.Lane1MeanPeak, 6);
        Assert.Equal(0.0, snapshot.Lane2MeanPeak);
    }

    // --- Capture integration -------------------------------------------------

    private const int SampleRate = 48000;

    private static DetectorResultSnapshot Result(float[] pcm, ulong startSample) =>
        new(TgSyncStatus.Synced, 28800, 0.125, Array.Empty<TgEvent>(), pcm, pcm.Length, startSample,
            false, false, false, 0.1f, 0f, 0f, 0f);

    private static DetectedEventUpdate AEvent(double sample)
    {
        var aEvent = new TgEvent { Type = TgEventType.A, SampleIndex = (ulong)sample, PeakValue = 0.5f };
        return new DetectedEventUpdate(aEvent, sample, new WatchMetricsUpdate());
    }

    private static void Feed(BeatSegmentCapture capture, ulong startSample, int length, params DetectedEventUpdate[] events)
    {
        var pcm = new float[length];
        Array.Fill(pcm, 0.05f);
        capture.Project(new DetectorMetricsBlockUpdate(
            Result(pcm, startSample),
            events,
            Array.Empty<DetectedEventUpdate>()));
    }

    [Fact]
    public void Capture_FeedsLanesBeforeTheFullWindowCompletesAndAlternates()
    {
        var capture = new BeatSegmentCapture(SampleRate, liftAngleDeg: 52.0);
        capture.SetSigmaAveraging(true);

        // Two beats 125 ms apart inside a 250 ms block: both 20 ms lane windows
        // are ready, while neither 400 ms segment window has completed.
        Feed(capture, 0, 12000, AEvent(1000), AEvent(7000));

        BeatSegmentsSnapshot? snapshot = capture.CurrentSnapshot();
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Segments);
        Assert.True(snapshot.Average.SigmaEnabled);
        Assert.Equal(1, snapshot.Average.Lane1Count);
        Assert.Equal(1, snapshot.Average.Lane2Count);
        Assert.Equal(BeatNoiseAverager.LanePoints, snapshot.Average.Lane1.Count);
    }

    [Fact]
    public void Capture_SigmaToggleResetsTheCycleAndBumpsTheVersion()
    {
        var capture = new BeatSegmentCapture(SampleRate, liftAngleDeg: 52.0);
        capture.SetSigmaAveraging(true);
        Feed(capture, 0, 12000, AEvent(1000), AEvent(7000));

        BeatSegmentsSnapshot before = capture.CurrentSnapshot()!;
        Assert.Equal(1, before.Average.Lane1Count);

        capture.SetSigmaAveraging(false);
        Feed(capture, 12000, 1440);

        BeatSegmentsSnapshot after = capture.CurrentSnapshot()!;
        Assert.True(after.Version > before.Version);
        Assert.False(after.Average.SigmaEnabled);
        Assert.Equal(0, after.Average.Lane1Count);
    }
}
