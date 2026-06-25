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

    // Synthesized real-beat PCM level and the injected half-beat artifact amplitude
    // as a fraction of it. The ratio is a PCM-domain knob (what we inject), NOT the
    // smoothed-envelope ratio the gate actually keys on -- only a convenient proxy.
    // Empirically the gate recovers the true rate for artifacts up to ~0.4x and lets
    // them alias above ~0.5x (pinned by StrongArtifact_AboveGateBand_StillAliases).
    private const double RealBeatPcmPeak = 0.40;
    private const double ArtifactPcmRatio = 0.30;

    // Clean 21600 stream with a weak damped-sine artifact injected at each half-beat.
    // At the default ratio the artifact passes the standard min-peak gate (and aliases
    // the rate to 43200) yet is clearly weaker than the real beats -- the band the
    // acquisition gate targets.
    private static float[] HalfBeatArtifactStream(double artifactRatio = ArtifactPcmRatio)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 21600;
        cfg.PcmPeakSignalLevel = RealBeatPcmPeak;
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
            AddClick(pcm, mid, peak: RealBeatPcmPeak * artifactRatio, freqHz: 4500.0, decayMs: 2.0);
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

    [Theory]
    [InlineData(18000)]
    [InlineData(21600)]
    [InlineData(28800)]
    [InlineData(36000)]
    public void Gate_IsNoOpOnCleanSignal(int bph)
    {
        // The on-by-default gate must not change detection on a clean signal at the
        // common rates: a clean stream has no anomalously-weak burst, so gate-on must
        // detect exactly what gate-off does (the true rate). Guards the default-on path.
        float[] pcm = CleanStream(bph, pcmPeak: 0.40, seconds: 8);
        int off = DetectedBph(pcm, gateFraction: 0.0);
        int on = DetectedBph(pcm, gateFraction: 0.35);
        Assert.Equal(bph, off);
        Assert.Equal(off, on);
    }

    [Fact]
    public void StrongArtifact_AboveGateBand_StillAliases()
    {
        // Coverage-edge pin: the gate only rejects bursts FAR weaker than the real
        // beats. A between-beat artifact at 0.5x the real beat level sits above the
        // gate band, so it still aliases the rate to 2x even with the gate on.
        // The recovery band is ~<=0.4x (AcquisitionGate_RecoversTrueRate uses 0.3x).
        float[] pcm = HalfBeatArtifactStream(artifactRatio: 0.50);
        Assert.Equal(43200, DetectedBph(pcm, gateFraction: 0.35));
    }

    [Fact]
    public void GenuineHighBeat_SurvivesGate()
    {
        // A real 43200 watch has uniform-amplitude beats at the half period. The
        // gate keys on a burst far weaker than its neighbors, so no beat is rejected
        // and the true high rate survives even with the gate on -- this guards the
        // app's on-by-default setting against downgrading a genuine high-beat watch.
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = 43200;
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.002;

        var synth = new WatchSynthStream(cfg);
        int n = Fs * 8;
        var pcm = new float[n];
        var block = new float[4096];
        int w = 0;
        while (w < n)
        {
            int sl = Math.Min(block.Length, n - w);
            synth.Generate(block.AsSpan(0, sl));
            Array.Copy(block, 0, pcm, w, sl);
            w += sl;
        }

        Assert.Equal(43200, DetectedBph(pcm, gateFraction: 0.35));
    }

    [Fact]
    public void Reacquisition_AfterLoudLockThenQuietDrop_StillLocks()
    {
        // F1 guard: a loud watch locks, then the signal stops (sync loss) and
        // returns much quieter (1/4 level). The gate keys on the decayed
        // ReferencePeak, so the stale loud level relaxes during the gap and the
        // quieter beats are not rejected -- re-lock to the true rate still succeeds
        // with the gate on. A raw undecayed accepted-peak median would stay loud
        // (history is not cleared on sync loss and gate rejects never call PushPeak)
        // and block reacquisition forever.
        float[] loud = CleanStream(bph: 21600, pcmPeak: 0.40, seconds: 4);
        var silence = new float[Fs * 5];
        float[] quiet = CleanStream(bph: 21600, pcmPeak: 0.10, seconds: 7);

        var pcm = new float[loud.Length + silence.Length + quiet.Length];
        Array.Copy(loud, 0, pcm, 0, loud.Length);
        Array.Copy(quiet, 0, pcm, loud.Length + silence.Length, quiet.Length);

        Assert.Equal(21600, DetectedBph(pcm, gateFraction: 0.35));
    }

    private static float[] CleanStream(int bph, double pcmPeak, int seconds)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = Fs;
        cfg.Bph = bph;
        cfg.PcmPeakSignalLevel = pcmPeak;
        cfg.NoisePeakSignalLevel = 0.002;

        var synth = new WatchSynthStream(cfg);
        int n = Fs * seconds;
        var pcm = new float[n];
        var block = new float[4096];
        int w = 0;
        while (w < n)
        {
            int sl = Math.Min(block.Length, n - w);
            synth.Generate(block.AsSpan(0, sl));
            Array.Copy(block, 0, pcm, w, sl);
            w += sl;
        }
        return pcm;
    }
}
