using System.Linq;
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
    public void SpectrogramProjector_UsesOneColormapForBothThemes()
    {
        // The colormap is theme-agnostic viridis (the theme only changes the empty
        // background, handled in the renderer). A blank window is the dB floor.
        Assert.Equal(SpectrogramFrameProjector.ColorLut, SpectrogramFrameProjector.ColorLutLight);

        var dark = new SpectrogramFrameProjector(sampleRate: 48000);
        var light = new SpectrogramFrameProjector(sampleRate: 48000, light: true);
        var darkFrame = new AnalysisFrame();
        dark.AppendSnapshot(darkFrame, force: true);
        var lightFrame = new AnalysisFrame();
        light.AppendSnapshot(lightFrame, force: true);

        uint floor = SpectrogramFrameProjector.ColorLut[0];
        Assert.All(darkFrame.SpectrogramImage!.Pixels, px => Assert.Equal(floor, px));
        Assert.All(lightFrame.SpectrogramImage!.Pixels, px => Assert.Equal(floor, px));
    }

    [Fact]
    public void SpectrogramProjector_SetColormap_MirrorsExistingImageAndRepublishes()
    {
        var projector = new SpectrogramFrameProjector(sampleRate: 48000);
        var initial = new AnalysisFrame();
        projector.AppendSnapshot(initial, force: true); // dark floor everywhere

        projector.SetColormap(light: true);
        var recolored = new AnalysisFrame();
        projector.AppendSnapshot(recolored); // recolor pending publishes despite cadence
        Assert.True(recolored.SpectrogramImageUpdated);
        Assert.All(
            recolored.SpectrogramImage!.Pixels,
            px => Assert.Equal(SpectrogramFrameProjector.ColorLutLight[0], px));

        // Selecting the same colormap again is a no-op: the image must not mirror
        // a second time (which would flip it back to the dark floor).
        projector.SetColormap(light: true);
        var unchanged = new AnalysisFrame();
        projector.AppendSnapshot(unchanged, force: true);
        Assert.All(
            unchanged.SpectrogramImage!.Pixels,
            px => Assert.Equal(SpectrogramFrameProjector.ColorLutLight[0], px));
    }

    [Fact]
    public void SpectrogramProjector_MirrorColormap_MapsBetweenThemesByIntensity()
    {
        uint[] dark = SpectrogramFrameProjector.ColorLut.ToArray();
        uint[] mapped = (uint[])dark.Clone();

        SpectrogramFrameProjector.MirrorColormap(mapped, toLight: true);
        Assert.Equal(SpectrogramFrameProjector.ColorLutLight, mapped);

        SpectrogramFrameProjector.MirrorColormap(mapped, toLight: false);
        Assert.Equal(dark, mapped); // round-trips back to the dark colormap
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
