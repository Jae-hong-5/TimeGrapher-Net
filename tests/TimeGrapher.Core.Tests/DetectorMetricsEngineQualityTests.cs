using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// The signal-quality classifier seam wired into the shared detector/metrics
/// engine. The classifier is an injected, read-only annotation: when none is
/// injected the engine skips it entirely (feature off), and when one is injected
/// it must not perturb detection (non-destructive condition monitoring).
/// </summary>
public sealed class DetectorMetricsEngineQualityTests
{
    private const int Bph = 28800;
    private const int SampleRate = 48000;

    private static DetectorMetricsEngineConfig CleanConfig() => new(
        SampleRate: SampleRate,
        LiftAngle: 52.0,
        AveragingPeriod: 2,
        UseCOnset: false,
        AutoBph: true,
        ManualBph: 0,
        HpfCutoffHz: 0.0);

    private static WatchSynthStream CleanSynth()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = SampleRate;
        cfg.Bph = Bph;
        cfg.NoisePeakSignalLevel = 0.0;
        cfg.PcmPeakSignalLevel = 0.40;
        return new WatchSynthStream(cfg);
    }

    private static (DetectorMetricsBlockUpdate Last, int Events) Run(DetectorMetricsEngine engine, int seconds)
    {
        WatchSynthStream synth = CleanSynth();
        float[] block = new float[4096];
        int remaining = SampleRate * seconds;
        int events = 0;
        DetectorMetricsBlockUpdate last = engine.Flush();
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            last = engine.Process(span);
            events += last.Result.Events.Count;
            remaining -= slice;
        }

        last = engine.Flush();
        events += last.Result.Events.Count;
        return (last, events);
    }

    [Fact]
    public void NoClassifierInjected_LeavesQualityAssessmentNull()
    {
        var engine = new DetectorMetricsEngine(CleanConfig());

        (DetectorMetricsBlockUpdate last, _) = Run(engine, seconds: 8);

        Assert.Null(last.Result.QualityAssessment);
    }

    [Fact]
    public void HeuristicClassifierInjected_AnnotatesCleanStreamAsGood()
    {
        var engine = new DetectorMetricsEngine(CleanConfig(), new HeuristicSignalQualityClassifier());

        (DetectorMetricsBlockUpdate last, _) = Run(engine, seconds: 8);

        Assert.NotNull(last.Result.QualityAssessment);
        Assert.Equal(SignalQualityClass.Good, last.Result.QualityAssessment!.Value.Class);
    }

    [Fact]
    public void ClassifierIsNonDestructive_DetectionIdenticalWithAndWithout()
    {
        var bare = new DetectorMetricsEngine(CleanConfig());
        (DetectorMetricsBlockUpdate bareLast, int bareEvents) = Run(bare, seconds: 8);

        var annotated = new DetectorMetricsEngine(CleanConfig(), new HeuristicSignalQualityClassifier());
        (DetectorMetricsBlockUpdate annLast, int annEvents) = Run(annotated, seconds: 8);

        // The two synth streams are seeded identically, so the only difference is
        // the injected classifier. Detection must be bit-for-bit unaffected.
        Assert.Equal(bareLast.Result.SyncStatus, annLast.Result.SyncStatus);
        Assert.Equal(bareLast.Result.DetectedBph, annLast.Result.DetectedBph);
        Assert.Equal(bareEvents, annEvents);
        Assert.Null(bareLast.Result.QualityAssessment);
        Assert.NotNull(annLast.Result.QualityAssessment);
    }
}
