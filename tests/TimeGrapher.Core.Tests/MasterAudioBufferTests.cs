using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class MasterAudioBufferTests
{
    // sampleRate 2 -> 60-sample ring (SecondsOfBuffer = 30) so wrap and overrun
    // paths are reachable with tiny blocks.
    private static MasterAudioBuffer SmallRing() => new(sampleRate: 2);

    private static float[] Sequence(int start, int count)
    {
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = start + i;
        }
        return data;
    }

    [Fact]
    public void CaptureTimestamp_ReturnsTheStampOfTheWriteContainingTheSample()
    {
        MasterAudioBuffer buffer = SmallRing();
        buffer.WriteSamples(Sequence(0, 10), captureTicks: 100); // samples 1..10
        buffer.WriteSamples(Sequence(10, 10), captureTicks: 200); // samples 11..20

        Assert.True(buffer.TryGetCaptureTimestamp(5, out long firstBlock));
        Assert.Equal(100, firstBlock);

        Assert.True(buffer.TryGetCaptureTimestamp(10, out long firstBlockEnd));
        Assert.Equal(100, firstBlockEnd);

        Assert.True(buffer.TryGetCaptureTimestamp(11, out long secondBlock));
        Assert.Equal(200, secondBlock);
    }

    [Fact]
    public void CaptureTimestamp_UnknownBeforeFirstWriteAndBeyondNewestSample()
    {
        MasterAudioBuffer buffer = SmallRing();
        Assert.False(buffer.TryGetCaptureTimestamp(1, out _));

        buffer.WriteSamples(Sequence(0, 10), captureTicks: 100);
        Assert.False(buffer.TryGetCaptureTimestamp(11, out _));

        buffer.Reset();
        Assert.False(buffer.TryGetCaptureTimestamp(1, out _));
    }

    [Fact]
    public void CaptureTimestamp_FallsBackToOldestSurvivingStampUnderDeepBacklog()
    {
        MasterAudioBuffer buffer = SmallRing();
        // 300 one-sample writes overflow the 256-entry stamp ring; the oldest
        // surviving stamp answers for evicted samples (latency lower bound).
        for (int i = 0; i < 300; i++)
        {
            buffer.WriteSamples(Sequence(i, 1), captureTicks: 1000 + i);
        }

        Assert.True(buffer.TryGetCaptureTimestamp(1, out long ticks));
        Assert.Equal(1000 + (300 - 256), ticks);
    }

    [Fact]
    public void WriteThenCopy_PreservesSampleOrderAcrossRingWrap()
    {
        MasterAudioBuffer buffer = SmallRing();
        var destination = new float[40];

        buffer.WriteSamples(Sequence(0, 40));
        MasterAudioBufferReadResult first =
            buffer.CopyAnalysisSamples(destination, buffer.GetSnapshot().TotalSamplesWritten);

        Assert.Equal(40, first.SamplesCopied);
        Assert.False(first.InputOverrun);
        Assert.Equal(Sequence(0, 40), destination);

        // The next 40 samples cross the 60-sample ring boundary.
        buffer.WriteSamples(Sequence(40, 40));
        MasterAudioBufferReadResult second =
            buffer.CopyAnalysisSamples(destination, buffer.GetSnapshot().TotalSamplesWritten);

        Assert.Equal(40, second.SamplesCopied);
        Assert.False(second.InputOverrun);
        Assert.Equal(0UL, second.InputSamplesDropped);
        Assert.Equal(Sequence(40, 40), destination);
    }

    [Fact]
    public void CopyAnalysisSamples_ReportsOverrunAndDropsOldest()
    {
        MasterAudioBuffer buffer = SmallRing();

        // 100 unread samples exceed the 60-sample ring: the oldest 40 are gone.
        buffer.WriteSamples(Sequence(0, 100));
        var destination = new float[60];
        MasterAudioBufferReadResult read =
            buffer.CopyAnalysisSamples(destination, buffer.GetSnapshot().TotalSamplesWritten);

        Assert.True(read.InputOverrun);
        Assert.Equal(40UL, read.InputSamplesDropped);
        Assert.Equal(60, read.SamplesCopied);
        Assert.Equal(Sequence(40, 60), destination);
    }

    [Fact]
    public void CopyAnalysisSamples_StopsAtSourceSnapshotEnd()
    {
        MasterAudioBuffer buffer = SmallRing();

        buffer.WriteSamples(Sequence(0, 10));
        ulong snapshot = buffer.GetSnapshot().TotalSamplesWritten;
        buffer.WriteSamples(Sequence(10, 10)); // written after the snapshot

        var destination = new float[20];
        MasterAudioBufferReadResult read = buffer.CopyAnalysisSamples(destination, snapshot);

        Assert.Equal(10, read.SamplesCopied);
        Assert.Equal(Sequence(0, 10), destination.Take(10).ToArray());
    }
}
