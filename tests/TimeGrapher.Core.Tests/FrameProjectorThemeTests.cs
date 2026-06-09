using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class FrameProjectorThemeTests
{
    private const uint White = 0xFFFFFFFFu;
    private const uint Black = 0xFF000000u;

    private static DetectorMetricsBlockUpdate UpdateWith(TgSyncStatus status) => new(
        new DetectorResultSnapshot(
            SyncStatus: status,
            DetectedBph: 0,
            MeasuredPeriodS: 0.0,
            Events: Array.Empty<TgEvent>(),
            ProcessedPcm: Array.Empty<float>(),
            ProcessedPcmLen: 0,
            ProcessedPcmStartSample: 0,
            SyncLostEvent: false,
            SyncAcquiredEvent: false,
            DetectorResetEvent: false,
            OnsetThreshold: 0f,
            MinPeakThreshold: 0f,
            NoiseFloor: 0f,
            ReferencePeak: 0f),
        Array.Empty<DetectedEventUpdate>());

    [Theory]
    [InlineData(TgSyncStatus.Synced, true)]
    [InlineData(TgSyncStatus.NotSynced, false)]
    [InlineData(TgSyncStatus.Mismatch, false)]
    public void ScopeRateProjector_SetsBeatSyncedFromSyncStatus(TgSyncStatus status, bool expected)
    {
        var projector = new ScopeRateFrameProjector(sampleRate: 48000, useCOnset: false, scopeSnapshotPointBudget: 8000);
        var frame = new AnalysisFrame();

        projector.Project(UpdateWith(status), frame);

        Assert.Equal(expected, frame.BeatSynced);
    }

    [Fact]
    public void AnalysisFrame_BeatSynced_DefaultsToFalse()
    {
        Assert.False(new AnalysisFrame().BeatSynced);
    }

    [Fact]
    public void SoundPrintProjector_SetBackgroundColor_RepublishesRetintedImage()
    {
        var projector = new SoundPrintFrameProjector(sampleRate: 48000, width: 64, height: 48, backgroundColor: White);

        projector.SetBackgroundColor(Black);

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame, force: true);

        Assert.True(frame.SoundImageUpdated);
        Assert.NotNull(frame.SoundImage);
        // Blank print (no beats yet) -> the whole image is the new background.
        Assert.All(frame.SoundImage!.Pixels, px => Assert.Equal(Black, px));
    }

    [Fact]
    public void SoundPrintProjector_PublishRotatesFixedBufferPool()
    {
        var projector = new SoundPrintFrameProjector(sampleRate: 48000, width: 64, height: 48, backgroundColor: White);

        // Publishing must not allocate a fresh buffer per frame: a published buffer
        // may be reused, but only after two newer publishes have gone out.
        var seen = new List<PixelBuffer>();
        for (int i = 0; i < 4; ++i)
        {
            var frame = new AnalysisFrame();
            projector.AppendSnapshot(frame, force: true);
            Assert.NotNull(frame.SoundImage);
            seen.Add(frame.SoundImage!);
        }

        Assert.NotSame(seen[0], seen[1]);
        Assert.NotSame(seen[0], seen[2]);
        Assert.NotSame(seen[1], seen[2]);
        Assert.Same(seen[0], seen[3]);
    }
}
