using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class SpectrogramFrameConsumerTests
{
    // The renderer needs the dynamic time-axis controls; the windowing tests only
    // exercise the consumer state machine, so dummy labels/caption suffice.
    private static SpectrogramRenderer CreateRenderer() => new(
        new Image(),
        new Image(),
        new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() },
        new[] { new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock(), new TextBlock() },
        new TextBlock(),
        new Border(),
        System.Array.Empty<Avalonia.Controls.Control>());

    [Fact]
    public void ObserveFrameCachesLatestSpectrogramImageAndResetClearsIt()
    {
        var renderer = CreateRenderer();
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
    public void ObserveFrameTakesTheProducerMonotonicColumnCount()
    {
        var consumer = new SpectrogramFrameConsumer(CreateRenderer());

        // The renderer's window crop needs the monotonic count of source columns
        // written this run; the producer stamps it on the frame as an absolute
        // value (not a delta), so the consumer reads it straight through — robust
        // to coalesced publishes and buffer wraps that a modulo live-column delta
        // would alias and undercount (which truncated the window on a late switch).
        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = new PixelBuffer(4, 2),
            SpectrogramLiveColumn = 2,
            SpectrogramTotalColumns = 2,
        });
        Assert.Equal(2L, consumer.TotalColumns);

        // A later publish well past a buffer wrap: the absolute total is taken as-is.
        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = new PixelBuffer(4, 2),
            SpectrogramLiveColumn = 1, // 4001 % 4 == 1, a wrapped live column
            SpectrogramTotalColumns = 4001,
        });
        Assert.Equal(4001L, consumer.TotalColumns);

        consumer.Reset(new AnalysisTabResetContext(48000, 10, 250));
        Assert.Equal(0L, consumer.TotalColumns);
    }

    [Fact]
    public void ObserveFrameProcessesANewPublishThatReusesAPooledBuffer()
    {
        var consumer = new SpectrogramFrameConsumer(CreateRenderer());

        // The projector rotates a fixed buffer pool, so a later publish can land on
        // the same PixelBuffer object as an earlier one. Dedup is by frame identity,
        // not buffer identity, so the new publish still updates the count/metadata.
        var pooled = new PixelBuffer(8, 2);

        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = pooled,
            SpectrogramTotalColumns = 2,
        });
        Assert.Equal(2L, consumer.TotalColumns);

        consumer.ObserveFrame(new AnalysisFrame
        {
            SpectrogramImageUpdated = true,
            SpectrogramImage = pooled, // same buffer object, new publish
            SpectrogramTotalColumns = 5,
        });
        Assert.Equal(5L, consumer.TotalColumns);
    }

    [Fact]
    public void TryRemapKeptImageMirrorsKeptImageOnlyWhenTheThemeChanges()
    {
        var renderer = CreateRenderer();
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
