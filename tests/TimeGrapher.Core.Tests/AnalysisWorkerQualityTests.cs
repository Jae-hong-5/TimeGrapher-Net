using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The AnalysisWorker transports the engine's signal-quality assessment onto
/// AnalysisFrame.SignalQuality and injects the (optional) classifier into the
/// shared engine. With no classifier the frame field stays null (feature off).
/// </summary>
public sealed class AnalysisWorkerQualityTests
{
    private const int SampleRate = 48000;
    private const int Bph = 21600;

    private static AnalysisFrame? RunCleanStream(ISignalQualityClassifier? classifier)
    {
        var buffer = new MasterAudioBuffer(SampleRate);
        var worker = new AnalysisWorker(buffer, new AnalysisWorker.Config
        {
            SampleRate = SampleRate,
            AveragingPeriod = 2,
            SoundImageWidth = 8,
            SoundImageHeight = 8,
            ScopeSnapshotPointBudget = 256,
            QualityClassifier = classifier,
        });

        AnalysisFrame? captured = null;
        worker.AnalysisFrameReady += frame => captured = frame;

        WatchSynthStreamConfig synthConfig = WatchSynthStreamConfig.Clean();
        synthConfig.SampleRateHz = SampleRate;
        synthConfig.Bph = Bph;
        synthConfig.NoisePeakSignalLevel = 0.0;
        synthConfig.PcmPeakSignalLevel = 0.40;
        var synth = new WatchSynthStream(synthConfig);

        var block = new float[4096];
        int remaining = SampleRate * 10;
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            buffer.WriteSamples(span);
            worker.HandleInputData();
            remaining -= slice;
        }

        return captured;
    }

    [Fact]
    public void InjectedClassifier_PopulatesFrameSignalQuality()
    {
        AnalysisFrame frame = Assert.IsType<AnalysisFrame>(RunCleanStream(new HeuristicSignalQualityClassifier()));

        Assert.NotNull(frame.SignalQuality);
        Assert.Equal(SignalQualityClass.Good, frame.SignalQuality!.Value.Class);
    }

    [Fact]
    public void NoClassifier_LeavesFrameSignalQualityNull()
    {
        AnalysisFrame frame = Assert.IsType<AnalysisFrame>(RunCleanStream(classifier: null));

        Assert.Null(frame.SignalQuality);
    }
}
