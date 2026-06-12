using TimeGrapher.Core.Detection.Scoring;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class BeatWindowFeaturesTests
{
    [Fact]
    public void BucketMaxDecimation_OnKnownRamp_IsExact()
    {
        // 256-sample ramp i/256: bucket k spans samples {2k, 2k+1}, max is
        // (2k+1)/256; after peak normalization (peak = 255/256) the feature
        // is (2k+1)/255.
        var window = new float[256];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = i / 256f;
        }
        var features = new float[BeatWindowFeatures.Points];

        Assert.True(BeatWindowFeatures.Extract(window, features));
        for (int k = 0; k < BeatWindowFeatures.Points; k++)
        {
            Assert.Equal((2 * k + 1) / 255f, features[k], 5);
        }
    }

    [Fact]
    public void PeakNormalization_MakesFeaturesGainInvariant()
    {
        var window = new float[300];
        var rng = new Random(42);
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = (float)rng.NextDouble();
        }
        var scaled = window.Select(v => v * 0.125f).ToArray();

        var a = new float[BeatWindowFeatures.Points];
        var b = new float[BeatWindowFeatures.Points];
        Assert.True(BeatWindowFeatures.Extract(window, a));
        Assert.True(BeatWindowFeatures.Extract(scaled, b));

        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i], 6);
        }
    }

    [Fact]
    public void WindowShorterThanPointCount_StillExtracts()
    {
        var window = new float[40];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = 1f + i;
        }
        var features = new float[BeatWindowFeatures.Points];

        Assert.True(BeatWindowFeatures.Extract(window, features));
        Assert.Equal(1f, features.Max());
    }

    [Fact]
    public void SilentOrEmptyWindow_ReturnsFalse()
    {
        var features = new float[BeatWindowFeatures.Points];
        Assert.False(BeatWindowFeatures.Extract(ReadOnlySpan<float>.Empty, features));
        Assert.False(BeatWindowFeatures.Extract(new float[512], features));
    }

    [Fact]
    public void UndersizedFeatureSpan_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            BeatWindowFeatures.Extract(new float[16], new float[BeatWindowFeatures.Points - 1]));
    }
}
