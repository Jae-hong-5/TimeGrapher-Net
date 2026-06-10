using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// QA latency evidence aggregation: per-leg averages and worst cases on the
/// injected tick clock, cumulative drop counters, and the 500 ms status throttle.
/// </summary>
public sealed class LatencyStatsTrackerTests
{
    // 1000 ticks per ms makes expected values readable.
    private static LatencyStatsTracker NewTracker() => new(ticksPerMs: 1000.0);

    private static AnalysisFrame Frame(long captureTicks, long processedTicks, ulong dropped = 0,
        ulong missedBeats = 0, uint syncLosses = 0)
    {
        return new AnalysisFrame
        {
            CaptureTimestamp = captureTicks,
            ProcessingCompletedTimestamp = processedTicks,
            InputSamplesDropped = dropped,
            MissedBeats = missedBeats,
            SyncLossCount = syncLosses,
        };
    }

    [Fact]
    public void Observe_ComputesPerLegAveragesAndWorstCase()
    {
        LatencyStatsTracker tracker = NewTracker();

        // Frame 1: cap→proc 10 ms, proc→disp 5 ms, e2e 15 ms.
        tracker.Observe(Frame(1_000, 11_000), coalescedFrames: 0, displayTicks: 16_000);
        // Frame 2: cap→proc 30 ms, proc→disp 15 ms, e2e 45 ms.
        tracker.Observe(Frame(100_000, 130_000), coalescedFrames: 2, displayTicks: 145_000);

        Assert.Equal(20.0, tracker.CapToProcAvgMs, 6);
        Assert.Equal(30.0, tracker.CapToProcMaxMs, 6);
        Assert.Equal(10.0, tracker.ProcToDispAvgMs, 6);
        Assert.Equal(15.0, tracker.ProcToDispMaxMs, 6);
        Assert.Equal(30.0, tracker.EndToEndAvgMs, 6);
        Assert.Equal(45.0, tracker.EndToEndMaxMs, 6);
        Assert.Equal(2UL, tracker.CoalescedFrames);
    }

    [Fact]
    public void Observe_SkipsLatencyLegsWhenTimestampsUnknownButStillCounts()
    {
        LatencyStatsTracker tracker = NewTracker();
        tracker.Observe(Frame(0, 0, dropped: 128, missedBeats: 3, syncLosses: 1), 1, 5_000);

        Assert.Equal(0, tracker.FrameCount);
        Assert.Equal(128UL, tracker.DroppedSamples);
        Assert.Equal(1UL, tracker.CoalescedFrames);
        Assert.Equal(3UL, tracker.MissedBeats);
        Assert.Equal(1U, tracker.SyncLosses);
    }

    [Fact]
    public void MissedBeatsAndSyncLosses_TrackTheCumulativeFrameValueNotASum()
    {
        LatencyStatsTracker tracker = NewTracker();
        tracker.Observe(Frame(0, 1_000, missedBeats: 2, syncLosses: 1), 0, 2_000);
        tracker.Observe(Frame(0, 1_000, missedBeats: 5, syncLosses: 1), 0, 2_000);

        // Core counters are session-cumulative; the tracker mirrors, never re-adds.
        Assert.Equal(5UL, tracker.MissedBeats);
        Assert.Equal(1U, tracker.SyncLosses);
    }

    [Fact]
    public void TryFormatStatus_ThrottlesToTheUpdateInterval()
    {
        LatencyStatsTracker tracker = NewTracker();
        Assert.Null(tracker.TryFormatStatus(0)); // nothing observed yet

        tracker.Observe(Frame(1_000, 11_000), 0, 16_000);
        string? first = tracker.TryFormatStatus(20_000);
        Assert.NotNull(first);

        // 100 ms later: throttled.
        Assert.Null(tracker.TryFormatStatus(120_000));
        // 600 ms later: due again.
        Assert.NotNull(tracker.TryFormatStatus(620_000));
    }

    [Fact]
    public void FormatStatus_ReportsAllMandatedCounters()
    {
        LatencyStatsTracker tracker = NewTracker();
        tracker.Observe(Frame(1_000, 11_000, dropped: 64, missedBeats: 2, syncLosses: 1), 3, 16_000);

        string text = tracker.FormatStatus();
        Assert.Contains("E2E 15/15 ms", text);
        Assert.Contains("cap→proc 10/10", text);
        Assert.Contains("disp 5/5", text);
        Assert.Contains("drop 64 smp / 3 frm", text);
        Assert.Contains("miss 2", text);
        Assert.Contains("sync−loss 1", text);
    }

    [Fact]
    public void FormatStatus_MarksWorstCaseAsLowerBoundAfterStampEviction()
    {
        LatencyStatsTracker tracker = NewTracker();
        var clamped = new AnalysisFrame
        {
            CaptureTimestamp = 1_000,
            ProcessingCompletedTimestamp = 11_000,
            CaptureTimestampIsLowerBound = true,
        };
        tracker.Observe(clamped, 0, 16_000);

        Assert.True(tracker.WorstCaseIsLowerBound);
        string text = tracker.FormatStatus();
        Assert.Contains("E2E 15/≥15 ms", text);
        Assert.Contains("cap→proc 10/≥10", text);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        LatencyStatsTracker tracker = NewTracker();
        tracker.Observe(Frame(1_000, 11_000, dropped: 64, missedBeats: 2, syncLosses: 1), 3, 16_000);
        tracker.Reset();

        Assert.Equal(0, tracker.FrameCount);
        Assert.Equal(0UL, tracker.DroppedSamples);
        Assert.Equal(0UL, tracker.CoalescedFrames);
        Assert.Equal(0UL, tracker.MissedBeats);
        Assert.Equal(0U, tracker.SyncLosses);
        Assert.Equal(0.0, tracker.EndToEndMaxMs);
    }
}
