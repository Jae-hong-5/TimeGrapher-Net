using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class SyntheticDetectorTests
{
    [Fact]
    public void CleanSyntheticStreamDetectsConfiguredBph()
    {
        const int sampleRate = 48000;
        const int expectedBph = 21600;

        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = sampleRate;
        synthConfig.Bph = expectedBph;
        synthConfig.NoisePeakAmplitude = 0.0;
        synthConfig.PcmPeakAmplitude = 0.40;

        var synth = new WatchSynthStream(synthConfig);
        TgConfig detectorConfig = TgConfig.Default();
        detectorConfig.SampleRate = sampleRate;
        detectorConfig.BphMode = TgBphMode.Auto;
        detectorConfig.SuppressPreSyncEvents = true;
        var detector = new TgDetector(detectorConfig);
        var result = new TgResult();
        float[] block = new float[4096];

        int remaining = sampleRate * 10;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            detector.Process(span, result);
            remaining -= slice;
        }

        detector.Flush(result);

        Assert.Equal(TgSyncStatus.Synced, result.SyncStatus);
        Assert.Equal(expectedBph, result.DetectedBph);
    }
}
