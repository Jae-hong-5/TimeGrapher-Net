using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The acquisition spurious-beat gate: a weak between-beat artifact sitting near
/// the half-beat aliases the detected BPH to 2x (the Rayleigh phase score folds
/// it in-phase at the half period, and the doubled event count drops the median
/// A-to-A interval so the plausibility floor stops rejecting the half-period
/// candidate). Enabling the gate rejects, while unsynced, a burst far weaker than
/// the recent accepted beats, restoring the true cadence. Default (fraction 0) is
/// off and covered as bit-identical by the golden-master tests.
/// </summary>
public sealed class AcquisitionPeakGateTests
{
    private const int Fs = 48000;

    // Clean 21600 stream with a weak damped-sine artifact injected at each
    // half-beat. At 0.30x the real PCM beat level the artifact is loud enough to
    // pass the standard min-peak gate (and alias the rate to 43200) yet clearly
    // weaker than the real beats -- the band the acquisition gate targets.
    private static float[] HalfBeatArtifactStream()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.002;

        var synth = new WatchSynthStream(cfg);
        int n = Fs * 10;
        var pcm = new float[n];
        var beatSamples = new List<double>();
        var block = new float[4096];
        var ev = new WatchSynthStreamEvent[64];
        int w = 0;
        while (w < n)
        {
            int sl = Math.Min(block.Length, n - w);
            WatchSynthStreamFillResult r = synth.FillF32(block.AsSpan(0, sl), ev);
            Array.Copy(block, 0, pcm, w, sl);
            for (int i = 0; i < r.EventsWritten; i++) beatSamples.Add(ev[i].TimeS * Fs);
            w += sl;
        }

        beatSamples.Sort();
        for (int i = 1; i < beatSamples.Count; i++)
        {
            int mid = (int)((beatSamples[i - 1] + beatSamples[i]) * 0.5);
            AddClick(pcm, mid, peak: 0.12, freqHz: 4500.0, decayMs: 2.0);
        }
        return pcm;
    }

    // Weak damped-sine click: peak * sin(w k) * exp(-k/tau), additive and clamped.
    private static void AddClick(float[] pcm, int start, double peak, double freqHz, double decayMs)
    {
        double tau = decayMs * 0.001 * Fs;
        double w = 2.0 * Math.PI * freqHz / Fs;
        int len = (int)(8.0 * tau);
        for (int k = 0; k < len && start + k < pcm.Length; k++)
        {
            double v = peak * Math.Sin(w * k) * Math.Exp(-k / tau);
            pcm[start + k] = (float)Math.Clamp(pcm[start + k] + v, -1.0, 1.0);
        }
    }

    private static int DetectedBph(float[] pcm, double gateFraction)
    {
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: Fs, LiftAngle: 52.0, AveragingPeriod: 2, UseCOnset: false,
            AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0,
            AcquisitionPeakGateFraction: gateFraction));
        int off = 0;
        while (off < pcm.Length)
        {
            int sl = Math.Min(4096, pcm.Length - off);
            engine.Process(pcm.AsSpan(off, sl));
            off += sl;
        }
        return engine.Flush().Result.DetectedBph;
    }

    [Fact]
    public void HalfBeatArtifact_AliasesToDoubleRate_WithoutGate()
    {
        float[] pcm = HalfBeatArtifactStream();
        Assert.Equal(43200, DetectedBph(pcm, gateFraction: 0.0));
    }

    [Fact]
    public void AcquisitionGate_RecoversTrueRate()
    {
        float[] pcm = HalfBeatArtifactStream();
        Assert.Equal(21600, DetectedBph(pcm, gateFraction: 0.35));
    }
}
