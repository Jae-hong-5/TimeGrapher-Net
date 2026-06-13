using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SpectrogramFrameConsumerTests
{
    [Fact]
    public void ObserveFrameCachesLatestSpectrogramImageAndResetClearsIt()
    {
        var renderer = new SpectrogramRenderer(new Image(), new Image());
        var consumer = new SpectrogramFrameConsumer(renderer);
        var spectrogramImage = new PixelBuffer(2, 2);
        spectrogramImage.Fill(Argb.Red);

        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = spectrogramImage,
        });

        Assert.Same(spectrogramImage, consumer.LatestSpectrogramImage);

        consumer.ObserveFrame(new AnalysisFrame()); // no publish: latest is kept
        Assert.Same(spectrogramImage, consumer.LatestSpectrogramImage);

        consumer.Reset(new AnalysisTabResetContext(48000, 10, 250));

        Assert.Null(consumer.LatestSpectrogramImage);
    }

    [Fact]
    public void TryRemapKeptImageMirrorsKeptImageOnlyWhenTheThemeChanges()
    {
        var renderer = new SpectrogramRenderer(new Image(), new Image());
        var consumer = new SpectrogramFrameConsumer(renderer);

        var image = new PixelBuffer(2, 1);
        image.SetPixel(0, 0, SpectrogramFrameProjector.ColorLut[0]);   // dB floor
        image.SetPixel(1, 0, SpectrogramFrameProjector.ColorLut[63]);  // 0 dB peak
        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = image,
        });

        // Toggling to the opposite theme recolors the kept image into a copy with
        // the same intensity remap the projector applies. Computed both ways so
        // the test is independent of the ambient theme variant.
        bool target = !PlotThemePalette.Current.IsLight;
        uint[] expected = (uint[])image.Pixels.Clone();
        SpectrogramFrameProjector.MirrorColormap(expected, target);

        Assert.True(consumer.TryRemapKeptImage(target, out PixelBuffer? remapped));
        Assert.NotSame(image, remapped);
        Assert.Equal(expected, remapped!.Pixels);

        // Re-applying the same theme is a no-op (no second remap).
        Assert.False(consumer.TryRemapKeptImage(target, out PixelBuffer? again));
        Assert.Null(again);
    }
}
