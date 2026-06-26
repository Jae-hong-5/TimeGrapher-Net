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

    [Fact]
    public void ApplyLiveParameters_SpeedingUpTheRateProducesMoreBeats()
    {
        // 28800 BPH = 8 beats/s, so a 1 s block holds ~8 events at the nominal rate.
        var stream = new WatchSynthStream(Clean(bph: 28800));
        var buf = new float[48000]; // 1 s
        var events = new WatchSynthStreamEvent[64];

        WatchSynthStreamFillResult nominal = stream.FillF32(buf, events);

        // +43200 s/day runs the watch 1.5x fast, shrinking the beat interval, so the
        // next second must contain strictly more beats once the new rate takes effect.
        stream.ApplyLiveParameters(
            rateErrorSPerDay: 43200.0, beatErrorMs: 0.0, watchAmplitudeDegrees: 270.0,
            noisePeakSignalLevel: 0.0,
            aClusterLevelScale: 1.0, bClusterLevelScale: 1.0, cClusterLevelScale: 1.0);

        WatchSynthStreamFillResult fast = stream.FillF32(buf, events);

        Assert.True(fast.EventsWritten > nominal.EventsWritten);
    }

    [Fact]
    public void ApplyLiveParameters_BeatErrorTakesEffectLive()
    {
        // Clean config has zero beat error: tick->tock and tock->tick intervals match,
        // so AppliedIntervalOffsetUs is 0 for every event.
        var stream = new WatchSynthStream(Clean(bph: 28800));
        var buf = new float[48000];
        var events = new WatchSynthStreamEvent[64];

        WatchSynthStreamFillResult baseline = stream.FillF32(buf, events);
        Assert.All(events.AsSpan(0, baseline.EventsWritten).ToArray(), e => Assert.Equal(0.0, e.AppliedIntervalOffsetUs));

        // +2 ms beat error must appear as a +/-2000 us alternating interval offset.
        stream.ApplyLiveParameters(
            rateErrorSPerDay: 0.0, beatErrorMs: 2.0, watchAmplitudeDegrees: 270.0,
            noisePeakSignalLevel: 0.0,
            aClusterLevelScale: 1.0, bClusterLevelScale: 1.0, cClusterLevelScale: 1.0);

        WatchSynthStreamFillResult withError = stream.FillF32(buf, events);

        Assert.Contains(
            events.AsSpan(0, withError.EventsWritten).ToArray(),
            e => Math.Abs(e.AppliedIntervalOffsetUs) > 1000.0);
    }

    [Fact]
    public void ApplyLiveParameters_InvalidUpdateIsIgnoredKeepingLastGoodValues()
    {
        // Two identical clean streams advanced in lockstep; one then receives a beat
        // error far larger than the beat interval (validation must reject it). The
        // rejected update must leave the stream untouched, so both stay bit-identical.
        var changed = new WatchSynthStream(Clean(bph: 28800, seed: 5));
        var control = new WatchSynthStream(Clean(bph: 28800, seed: 5));
        var bufChanged = new float[48000];
        var bufControl = new float[48000];

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        changed.ApplyLiveParameters(
            rateErrorSPerDay: 0.0, beatErrorMs: 100_000.0, watchAmplitudeDegrees: 270.0,
            noisePeakSignalLevel: 0.0,
            aClusterLevelScale: 1.0, bClusterLevelScale: 1.0, cClusterLevelScale: 1.0);

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        Assert.Equal(bufControl, bufChanged);
    }

    [Fact]
    public void ApplyLiveParameters_ClusterScaleTakesEffectLive()
    {
        // Realistic packet on (per-cluster scales only bite there) with every
        // stochastic component off, so shrinking the C cluster on one stream is the
        // only difference and the next block must diverge from the untouched control.
        var changed = new WatchSynthStream(RealisticDeterministic());
        var control = new WatchSynthStream(RealisticDeterministic());
        var bufChanged = new float[48000];
        var bufControl = new float[48000];

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        changed.ApplyLiveParameters(
            rateErrorSPerDay: 0.0, beatErrorMs: 0.0, watchAmplitudeDegrees: 270.0,
            noisePeakSignalLevel: 0.0,
            aClusterLevelScale: 1.0, bClusterLevelScale: 1.0, cClusterLevelScale: 0.2);

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        Assert.NotEqual(bufControl, bufChanged);
    }

    [Fact]
    public void ApplyLiveParameters_NoiseLevelTakesEffectLive()
    {
        var changed = new WatchSynthStream(Clean(bph: 28800, seed: 9));
        var control = new WatchSynthStream(Clean(bph: 28800, seed: 9));
        var bufChanged = new float[48000];
        var bufControl = new float[48000];

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        changed.ApplyLiveParameters(
            rateErrorSPerDay: 0.0, beatErrorMs: 0.0, watchAmplitudeDegrees: 270.0,
            noisePeakSignalLevel: 0.05,
            aClusterLevelScale: 1.0, bClusterLevelScale: 1.0, cClusterLevelScale: 1.0);

        changed.Generate(bufChanged);
        control.Generate(bufControl);

        Assert.NotEqual(bufControl, bufChanged);
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
