using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// In-process mirrors of the verifier's two strongest adverse A/B rows
/// (harness-test alignment convention), so `dotnet test` alone proves the
/// baseline weakness AND the robust-profile recovery without running the
/// Verify executable:
///  - impulse-dos: full-scale impulses once a second over a quiet watch.
///    Baseline storms detector resets (W-3) and never holds lock; the robust
///    profile rides it out.
///  - quiet-step: a 0.13x gain step after 6 s. Baseline latches the
///    reference high and never re-locks (W-4(b)); the robust profile decays
///    and re-acquires.
/// </summary>
public sealed class DetectorStressScenarioTests
{
    private sealed record StressResult(TgSyncStatus FinalSync, int DetectedBph, int Resets);

    private static StressResult Run(
        bool robust, double pcmPeak, double noisePeak, int bph, int seconds,
        double impulseRate = 0.0, double impulseAmp = 0.0,
        double gainStepAtS = 0.0, double gainStepFactor = 1.0)
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = 48000;
        cfg.Bph = bph;
        cfg.PcmPeakAmplitude = pcmPeak;
        cfg.NoisePeakAmplitude = noisePeak;
        if (impulseRate > 0.0)
        {
            cfg.ImpulseNoiseRatePerSecond = impulseRate;
            cfg.ImpulseNoisePeakAmplitude = impulseAmp;
        }

        var synth = new WatchSynthStream(cfg);
        var engine = new DetectorMetricsEngine(new DetectorMetricsEngineConfig(
            SampleRate: 48000,
            LiftAngle: 52.0,
            AveragingPeriod: 2,
            UseCOnset: false,
            AutoBph: true,
            ManualBph: 0,
            HpfCutoffHz: 0.0,
            DetectorOptions: robust ? TgDetectorOptions.Robust() : null,
            EventGate: robust ? new BeatEventGateConfig(new PllMatchGate()) : null));

        int resets = 0;
        var block = new float[4096];
        DetectorMetricsBlockUpdate update = default!;
        long total = 48000L * seconds;
        long done = 0;
        long stepAt = gainStepAtS > 0.0 ? (long)(gainStepAtS * 48000) : long.MaxValue;
        while (done < total)
        {
            int slice = (int)Math.Min(block.Length, total - done);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            for (int i = 0; i < slice; i++)
            {
                if (done + i >= stepAt)
                {
                    span[i] *= (float)gainStepFactor;
                }
            }
            update = engine.Process(span);
            if (update.Result.DetectorResetEvent)
            {
                resets++;
            }
            done += slice;
        }
        update = engine.Flush();
        if (update.Result.DetectorResetEvent)
        {
            resets++;
        }
        return new StressResult(update.Result.SyncStatus, update.Result.DetectedBph, resets);
    }

    [Fact]
    public void ImpulseDos_BaselineResetStorm_RobustHoldsLock()
    {
        StressResult baseline = Run(robust: false, pcmPeak: 0.03, noisePeak: 0.004,
            bph: 21600, seconds: 16, impulseRate: 1.0, impulseAmp: 0.95);
        StressResult robust = Run(robust: true, pcmPeak: 0.03, noisePeak: 0.004,
            bph: 21600, seconds: 16, impulseRate: 1.0, impulseAmp: 0.95);

        // W-3 pin: the baseline reset storm must stay reproducible.
        Assert.True(baseline.Resets >= 3, $"baseline resets {baseline.Resets}");
        Assert.NotEqual(TgSyncStatus.Synced, baseline.FinalSync);

        // Robust profile: no resets, lock held at the true rate.
        Assert.True(robust.Resets <= 1, $"robust resets {robust.Resets}");
        Assert.Equal(TgSyncStatus.Synced, robust.FinalSync);
        Assert.Equal(21600, robust.DetectedBph);
    }

    [Fact]
    public void QuietStep_BaselineNeverRelocks_RobustReacquires()
    {
        StressResult baseline = Run(robust: false, pcmPeak: 0.60, noisePeak: 0.01,
            bph: 21600, seconds: 16, gainStepAtS: 6.0, gainStepFactor: 0.13);
        StressResult robust = Run(robust: true, pcmPeak: 0.60, noisePeak: 0.01,
            bph: 21600, seconds: 16, gainStepAtS: 6.0, gainStepFactor: 0.13);

        // W-4(b) pin: the baseline must still lose the watch for good.
        Assert.NotEqual(TgSyncStatus.Synced, baseline.FinalSync);

        // Robust profile: reference decays, detection resumes, lock returns.
        Assert.Equal(TgSyncStatus.Synced, robust.FinalSync);
        Assert.Equal(21600, robust.DetectedBph);
    }
}
