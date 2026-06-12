using TimeGrapher.App;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection.Scoring;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the run-settings -> worker-config mapping of the robust-detection
/// preset: unchecked must wire null/null (the bit-identical original
/// pipeline - the user-facing leg of the fidelity invariant), checked must
/// wire exactly the composition the verifier's robust profile froze by A/B
/// measurement (Robust() options + PLL veto gate).
/// </summary>
public sealed class AnalysisRunSettingsTests
{
    private static AnalysisRunSettings NewSettings(bool robustDetection) => new(
        SampleRate: 48000,
        LiftAngle: 52.0,
        AveragingPeriod: 2,
        UseCOnset: false,
        AutoBph: true,
        ManualBph: 0,
        HpfCutoffHz: 200.0,
        SoundImageWidth: 100,
        SoundImageHeight: 100,
        ScopeSnapshotPointBudget: 8000,
        RobustDetection: robustDetection);

    [Fact]
    public void RobustDetectionOff_WiresTheOriginalPipeline()
    {
        AnalysisWorker.Config config = NewSettings(robustDetection: false)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Null(config.DetectorOptions);
        Assert.Null(config.EventGate);
    }

    [Fact]
    public void RobustDetectionOn_WiresTheMeasuredRobustPreset()
    {
        AnalysisWorker.Config config = NewSettings(robustDetection: true)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.NotNull(config.DetectorOptions);
        Assert.True(config.DetectorOptions!.EnableAdaptiveFloor);
        Assert.True(config.DetectorOptions.EnableRegimeGuard);
        Assert.IsType<PllMatchGate>(config.EventGate);
    }
}
