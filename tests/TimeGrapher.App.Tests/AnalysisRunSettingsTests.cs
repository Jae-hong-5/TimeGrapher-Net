using TimeGrapher.App;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the run-settings -> worker-config policy: the weak-A onset rescue and the
/// acquisition spurious-beat gate each stay behind their toggle and default on.
/// </summary>
public sealed class AnalysisRunSettingsTests
{
    private static AnalysisRunSettings NewSettings(
        int analysisBlockSize = 4096, bool weakAOnsetRescue = true,
        int weakAOnsetRescueStrengthStep = WeakAOnsetRescueStrengthPolicy.StandardStep,
        bool spuriousBeatRejection = true) => new(
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
        WeakAOnsetRescue: weakAOnsetRescue,
        WeakAOnsetRescueStrengthStep: weakAOnsetRescueStrengthStep,
        SpuriousBeatRejection: spuriousBeatRejection);

    [Fact]
    public void Default_WiresTheRescue()
    {
        AnalysisWorker.Config config = NewSettings()
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(1.0, config.PhaseGuideOnsetRescueScale, precision: 12);
    }

    [Fact]
    public void WeakAOnsetRescueOff_ClearsTheRescueScale()
    {
        AnalysisWorker.Config config = NewSettings(
                weakAOnsetRescue: false,
                weakAOnsetRescueStrengthStep: WeakAOnsetRescueStrengthPolicy.MaxStep)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(0.0, config.PhaseGuideOnsetRescueScale);
    }

    [Theory]
    [InlineData(WeakAOnsetRescueStrengthPolicy.MinStep, 1.25)]
    [InlineData(WeakAOnsetRescueStrengthPolicy.StandardStep, 1.0)]
    [InlineData(WeakAOnsetRescueStrengthPolicy.MaxStep, 0.75)]
    public void WeakAOnsetRescueStrengthStep_MapsToDetectorScale(int step, double expected)
    {
        AnalysisWorker.Config config = NewSettings(weakAOnsetRescueStrengthStep: step)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(expected, config.PhaseGuideOnsetRescueScale, precision: 12);
    }

    [Theory]
    [InlineData(-1, 1.25)]
    [InlineData(11, 0.75)]
    public void WeakAOnsetRescueStrengthStep_ClampsAtTheDetectorBoundary(int step, double expected)
    {
        AnalysisWorker.Config config = NewSettings(weakAOnsetRescueStrengthStep: step)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(expected, config.PhaseGuideOnsetRescueScale, precision: 12);
    }

    [Fact]
    public void Default_WiresTheAcquisitionGate()
    {
        AnalysisWorker.Config config = NewSettings()
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(0.35, config.AcquisitionPeakGateFraction);
    }

    [Fact]
    public void SpuriousBeatRejectionOff_ClearsTheAcquisitionGateFraction()
    {
        AnalysisWorker.Config config = NewSettings(spuriousBeatRejection: false)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(0.0, config.AcquisitionPeakGateFraction);
    }

    [Fact]
    public void AnalysisBlockSize_FlowsToWorkerConfig()
    {
        AnalysisWorker.Config config = NewSettings(analysisBlockSize: 8192)
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Equal(8192, config.AnalysisBlockSize);
    }

    [Fact]
    public void NoClassifier_LeavesTheWindowPathOff()
    {
        AnalysisWorker.Config config = NewSettings()
            .ToWorkerConfig(sessionId: 1, sampleWriter: null);

        Assert.Null(config.QualityClassifier);
    }

    [Fact]
    public void InjectedClassifier_FlowsToWorkerConfig()
    {
        var classifier = new HeuristicSignalQualityClassifier();

        AnalysisWorker.Config config = NewSettings()
            .ToWorkerConfig(sessionId: 1, sampleWriter: null, qualityClassifier: classifier);

        Assert.Same(classifier, config.QualityClassifier);
    }
}
