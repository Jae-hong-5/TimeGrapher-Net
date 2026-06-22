using TimeGrapher.App;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Detection.Scoring;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the run-settings -> worker-config policy: the PLL event veto stays
/// behind the checkbox and defaults off.
/// </summary>
public sealed class AnalysisRunSettingsTests
{
    private static AnalysisRunSettings NewSettings(bool pllEventVeto, int analysisBlockSize = 4096) => new(
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
        PllEventVeto: pllEventVeto,
        AnalysisBlockSize: analysisBlockSize);

    [Fact]
    public void Default_DoesNotWireTheVeto()
    {
        AnalysisWorker.Config config = NewSettings(pllEventVeto: false)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Null(config.EventGate);
    }

    [Fact]
    public void PllEventVetoOn_AddsTheGate()
    {
        AnalysisWorker.Config config = NewSettings(pllEventVeto: true)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.IsType<PllMatchGate>(config.EventGate);
    }

    [Fact]
    public void AnalysisBlockSize_FlowsToWorkerConfig()
    {
        AnalysisWorker.Config config = NewSettings(pllEventVeto: false, analysisBlockSize: 8192)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(8192, config.AnalysisBlockSize);
    }
}
