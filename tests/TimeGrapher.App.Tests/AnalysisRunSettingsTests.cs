using TimeGrapher.App;
using TimeGrapher.Core.Analysis;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the run-settings -> worker-config policy: the weak-A onset rescue stays
/// behind its toggle and defaults off.
/// </summary>
public sealed class AnalysisRunSettingsTests
{
    private static AnalysisRunSettings NewSettings(
        int analysisBlockSize = 4096, bool weakAOnsetRescue = false) => new(
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
        AnalysisBlockSize: analysisBlockSize,
        WeakAOnsetRescue: weakAOnsetRescue);

    [Fact]
    public void Default_DoesNotWireTheRescue()
    {
        AnalysisWorker.Config config = NewSettings()
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(0.0, config.PhaseGuideOnsetRescueScale);
    }

    [Fact]
    public void WeakAOnsetRescueOn_SetsTheRescueScale()
    {
        AnalysisWorker.Config config = NewSettings(weakAOnsetRescue: true)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(1.0, config.PhaseGuideOnsetRescueScale);
    }

    [Fact]
    public void AnalysisBlockSize_FlowsToWorkerConfig()
    {
        AnalysisWorker.Config config = NewSettings(analysisBlockSize: 8192)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(8192, config.AnalysisBlockSize);
    }
}
