using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

public sealed class WatchSynthStreamTests
{
    private static WatchSynthStreamConfig Clean(int bph = 28800, ulong seed = 12345, double noise = 0.0)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = 48000;
        cfg.Bph = bph;
        cfg.Seed = seed;
        cfg.PcmPeakSignalLevel = 0.40;
        // Seed only changes output when a stochastic component is enabled (noise/jitter);
        // a fully clean packet is deterministic regardless of seed.
        cfg.NoisePeakSignalLevel = noise;
        return cfg;
    }

    [Fact]
    public void Generate_ProducesNonSilentOutput()
    {
        var synth = new WatchSynthStream(Clean());
        var block = new float[48000 * 2]; // 2 s — long enough to contain several beats
        synth.Generate(block);

        Assert.Contains(block, sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void Generate_IsDeterministicForSameSeed()
    {
        var a = new WatchSynthStream(Clean(seed: 777, noise: 0.05));
        var b = new WatchSynthStream(Clean(seed: 777, noise: 0.05));
        var bufA = new float[48000];
        var bufB = new float[48000];

        a.Generate(bufA);
        b.Generate(bufB);

        Assert.Equal(bufA, bufB);
    }

    [Fact]
    public void Generate_DiffersForDifferentSeed()
    {
        var a = new WatchSynthStream(Clean(seed: 1, noise: 0.05));
        var b = new WatchSynthStream(Clean(seed: 2, noise: 0.05));
        var bufA = new float[48000];
        var bufB = new float[48000];

        a.Generate(bufA);
        b.Generate(bufB);

        Assert.NotEqual(bufA, bufB);
    }

    // Deterministic realistic A/B/C packet: realistic packet structure on, but every
    // stochastic component (jitter/noise/variation/drift/resonance/wander) left off so
    // per-cluster amplitude scaling can be measured in isolation.
    private static WatchSynthStreamConfig RealisticDeterministic(int bph = 28800)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = 48000;
        cfg.Bph = bph;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.0;
        cfg.EnableRealisticPacket = 1;
        return cfg;
    }

    private static (float[] Pcm, ulong FirstOnset) GenerateWithFirstOnset(WatchSynthStreamConfig cfg, int samples)
    {
        var synth = new WatchSynthStream(cfg);
        var pcm = new float[samples];
        var events = new WatchSynthStreamEvent[64];
        WatchSynthStreamFillResult r = synth.FillF32(pcm, events);
        Assert.True(r.EventsWritten > 0, "expected at least one beat event");
        return (pcm, events[0].SampleIndex);
    }

    private static float WindowPeak(float[] pcm, ulong onset, double startMs, double endMs, int fs)
    {
        int a = (int)onset + (int)(startMs * 1e-3 * fs);
        int b = (int)onset + (int)(endMs * 1e-3 * fs);
        float peak = 0f;
        for (int i = a; i < b && i < pcm.Length; i++)
            peak = Math.Max(peak, Math.Abs(pcm[i]));
        return peak;
    }

    [Fact]
    public void ClusterLevelScales_DefaultToOne()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        Assert.Equal(1.0, cfg.AClusterLevelScale);
        Assert.Equal(1.0, cfg.BClusterLevelScale);
        Assert.Equal(1.0, cfg.CClusterLevelScale);
    }

    [Fact]
    public void ZeroAClusterScale_SuppressesOnsetWindowEnergy()
    {
        const int fs = 48000;
        var (pcmDefault, onset) = GenerateWithFirstOnset(RealisticDeterministic(), fs);
        WatchSynthStreamConfig weakA = RealisticDeterministic();
        weakA.AClusterLevelScale = 0.0;
        var (pcmWeakA, onsetWeakA) = GenerateWithFirstOnset(weakA, fs);

        // Onset window = first 1 ms after A; only the A cluster lobes live here.
        float defaultPeak = WindowPeak(pcmDefault, onset, 0.0, 1.0, fs);
        float weakPeak = WindowPeak(pcmWeakA, onsetWeakA, 0.0, 1.0, fs);

        Assert.True(defaultPeak > 0.01f, $"expected onset energy by default, got {defaultPeak}");
        Assert.True(weakPeak < defaultPeak * 0.1f, $"A scale 0 should suppress onset energy: {weakPeak} vs {defaultPeak}");
    }

    [Fact]
    public void Event_CarriesClusterLevelScales()
    {
        WatchSynthStreamConfig cfg = RealisticDeterministic();
        cfg.AClusterLevelScale = 0.5;
        cfg.BClusterLevelScale = 3.0;
        cfg.CClusterLevelScale = 0.25;

        var synth = new WatchSynthStream(cfg);
        var pcm = new float[48000];
        var events = new WatchSynthStreamEvent[8];
        WatchSynthStreamFillResult r = synth.FillF32(pcm, events);

        Assert.True(r.EventsWritten > 0, "expected at least one beat event");
        Assert.Equal(0.5, events[0].AClusterLevelScale);
        Assert.Equal(3.0, events[0].BClusterLevelScale);
        Assert.Equal(0.25, events[0].CClusterLevelScale);
    }

    [Fact]
    public void BoostedBClusterScale_IncreasesMiddleWindowEnergy()
    {
        const int fs = 48000;
        var (pcmDefault, onset) = GenerateWithFirstOnset(RealisticDeterministic(), fs);
        WatchSynthStreamConfig loudB = RealisticDeterministic();
        loudB.BClusterLevelScale = 4.0;
        var (pcmLoudB, onsetLoudB) = GenerateWithFirstOnset(loudB, fs);

        // Middle (B) window: the B lobes sit at 2.15 ms and 3.05 ms after onset.
        float defaultPeak = WindowPeak(pcmDefault, onset, 2.0, 3.5, fs);
        float loudPeak = WindowPeak(pcmLoudB, onsetLoudB, 2.0, 3.5, fs);

        Assert.True(defaultPeak > 0.005f, $"expected B energy by default, got {defaultPeak}");
        Assert.True(loudPeak > defaultPeak * 2.0f, $"B scale 4 should raise B energy: {loudPeak} vs {defaultPeak}");
    }
}
