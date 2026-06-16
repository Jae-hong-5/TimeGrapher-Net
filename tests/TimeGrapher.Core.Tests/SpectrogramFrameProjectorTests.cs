using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// STFT spectrogram projector contract: a sinusoid lights up the expected
/// frequency row through the inferno LUT, the live-edge cursor honors its
/// deadline knob, publishing follows the 100 ms cadence, and snapshots rotate
/// through the fixed three-buffer pool instead of allocating.
/// </summary>
public sealed class SpectrogramFrameProjectorTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void ProjectorDerivesStftGeometryFromSampleRate()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);

        Assert.Equal(1024, projector.FftSize);   // pow2 nearest 21 ms @ 48 kHz
        Assert.Equal(512, projector.HopSize);
        Assert.Equal(257, projector.Height);     // bins 0..12 kHz inclusive
        Assert.Equal(938, projector.Width);      // 10 s of hop columns
    }

    [Fact]
    public void ProcessSamples_RendersSinusoidIntoExpectedFrequencyRow()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);
        FeedSine(projector, frequencyHz: 3000.0, amplitude: 0.5, seconds: 1.0);

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame, force: true);

        Assert.True(frame.SpectrogramImageUpdated);
        PixelBuffer image = frame.SpectrogramImage!;
        int bin = (int)Math.Round(3000.0 * projector.FftSize / SampleRate);
        int hotRow = image.Height - 1 - bin;
        int coldRow = image.Height - 1 - (bin + 40);
        const int column = 10; // a completed column well behind the live edge

        Assert.True(LutIndexOf(image.GetPixel(column, hotRow)) >= 48,
            "the 3 kHz row must read near the top of the intensity scale");
        Assert.True(LutIndexOf(image.GetPixel(column, coldRow)) <= 4,
            "a distant frequency row must stay near the dB floor");
    }

    [Fact]
    public void ProcessSamples_DrawsLiveEdgeCursorOnlyWhileEnabled()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);
        FeedSine(projector, frequencyHz: 1000.0, amplitude: 0.5, seconds: 0.1);
        Assert.Equal(
            SpectrogramFrameProjector.LiveEdgeColor,
            Snapshot(projector).GetPixel(projector.CurrentColumn, 0));

        projector.SetLivePreviewEnabled(false);
        FeedSine(projector, frequencyHz: 1000.0, amplitude: 0.5, seconds: 0.1);

        Assert.NotEqual(
            SpectrogramFrameProjector.LiveEdgeColor,
            Snapshot(projector).GetPixel(projector.CurrentColumn, 0));
    }

    [Fact]
    public void AppendSnapshot_HonorsPublishCadence()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);

        var first = new AnalysisFrame();
        projector.AppendSnapshot(first);
        Assert.True(first.SpectrogramImageUpdated); // publish timer not running yet

        var second = new AnalysisFrame();
        projector.AppendSnapshot(second);
        Assert.False(second.SpectrogramImageUpdated); // within the 100 ms interval
        Assert.Null(second.SpectrogramImage);

        var forced = new AnalysisFrame();
        projector.AppendSnapshot(forced, force: true);
        Assert.True(forced.SpectrogramImageUpdated);
    }

    [Fact]
    public void AppendSnapshot_ReportsMonotonicColumnCountAcrossTheBufferWrap()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);

        // Feed more than the 10 s wrap buffer holds so the live column wraps but the
        // monotonic total keeps climbing past Width — the renderer relies on this
        // absolute count instead of reconstructing it from the modulo live column.
        FeedSine(projector, frequencyHz: 1000.0, amplitude: 0.5, seconds: 11.0);

        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame, force: true);

        Assert.True(frame.SpectrogramTotalColumns > projector.Width,
            "the monotonic total must climb past the wrap-buffer width");
        // total % width is exactly the live (next-to-write) column.
        Assert.Equal(frame.SpectrogramLiveColumn, (int)(frame.SpectrogramTotalColumns % projector.Width));
    }

    [Fact]
    public void AppendSnapshot_RotatesThroughFixedPublishPool()
    {
        var projector = new SpectrogramFrameProjector(SampleRate);

        PixelBuffer[] published = Enumerable.Range(0, 4).Select(_ =>
        {
            var frame = new AnalysisFrame();
            projector.AppendSnapshot(frame, force: true);
            return frame.SpectrogramImage!;
        }).ToArray();

        Assert.NotSame(published[0], published[1]);
        Assert.NotSame(published[1], published[2]);
        Assert.Same(published[0], published[3]); // pool of three, recycled in order
    }

    private static void FeedSine(
        SpectrogramFrameProjector projector, double frequencyHz, double amplitude, double seconds)
    {
        var block = new float[4096];
        int total = (int)(SampleRate * seconds);
        int generated = 0;
        while (generated < total)
        {
            int slice = Math.Min(block.Length, total - generated);
            for (int i = 0; i < slice; i++)
            {
                block[i] = (float)(amplitude *
                    Math.Sin(2.0 * Math.PI * frequencyHz * (generated + i) / SampleRate));
            }

            projector.ProcessSamples(block.AsSpan(0, slice));
            generated += slice;
        }
    }

    private static PixelBuffer Snapshot(SpectrogramFrameProjector projector)
    {
        var frame = new AnalysisFrame();
        projector.AppendSnapshot(frame, force: true);
        return frame.SpectrogramImage!;
    }

    private static int LutIndexOf(uint color)
    {
        IReadOnlyList<uint> lut = SpectrogramFrameProjector.ColorLut;
        for (int i = 0; i < lut.Count; i++)
        {
            if (lut[i] == color)
            {
                return i;
            }
        }

        throw new Xunit.Sdk.XunitException($"Color 0x{color:X8} is not a LUT entry.");
    }
}
