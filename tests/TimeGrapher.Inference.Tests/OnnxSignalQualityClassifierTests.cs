using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Analysis.Quality;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;
using TimeGrapher.Inference;
using Xunit;

namespace TimeGrapher.Inference.Tests;

/// <summary>
/// The ONNX signal-quality classifier (the TinyML Strategy behind the
/// ISignalQualityClassifier seam). These tests live in their own project so Core
/// and Core.Tests stay free of any ONNX dependency. They pin: the embedded model
/// loads and classifies, the class-order sidecar is read correctly (the model
/// agrees with the heuristic on clear-cut feature vectors), confidence is a
/// probability, results are deterministic, the pre-sync guard matches the
/// heuristic, the model is non-destructive end-to-end on real synth streams, and
/// the composition-root fallback works.
/// </summary>
public sealed class OnnxSignalQualityClassifierTests
{
    // Feature vectors planted deep inside each heuristic class region so the
    // learned (approximate) model and the heuristic must agree. Mirrors the
    // boundaries pinned by HeuristicSignalQualityClassifierTests.
    private static SignalQualityFeatures Good() =>
        new(SnrDb: 32f, PeakMarginRatio: 4f, NoiseFloorLevel: 0.001f, IntervalJitterCv: 0.005f,
            PeakLevelCv: 0.02f, MissedBeatRate: 0f, SyncLossRate: 0f, SyncedFraction: 1f);

    private static SignalQualityFeatures Noisy() =>
        new(SnrDb: 18f, PeakMarginRatio: 4f, NoiseFloorLevel: 0.01f, IntervalJitterCv: 0.005f,
            PeakLevelCv: 0.05f, MissedBeatRate: 0f, SyncLossRate: 0f, SyncedFraction: 1f);

    private static SignalQualityFeatures Weak() =>
        new(SnrDb: 5f, PeakMarginRatio: 4f, NoiseFloorLevel: 0.05f, IntervalJitterCv: 0.005f,
            PeakLevelCv: 0.02f, MissedBeatRate: 0f, SyncLossRate: 0f, SyncedFraction: 1f);

    private static SignalQualityFeatures Unstable() =>
        new(SnrDb: 32f, PeakMarginRatio: 4f, NoiseFloorLevel: 0.001f, IntervalJitterCv: 0.005f,
            PeakLevelCv: 0.02f, MissedBeatRate: 0.12f, SyncLossRate: 0f, SyncedFraction: 1f);

    [Fact]
    public void LoadDefault_LoadsTheEmbeddedModelAndClassifies()
    {
        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();

        SignalQualityAssessment a = model.Classify(Good());

        Assert.Equal(SignalQualityClass.Good, a.Class);
        Assert.InRange(a.Confidence, 0f, 1f);
        Assert.True(a.Confidence > 0f);
    }

    [Theory]
    [InlineData("good")]
    [InlineData("noisy")]
    [InlineData("weak")]
    [InlineData("unstable")]
    public void Classify_AgreesWithHeuristic_OnClearCutVectors(string which)
    {
        SignalQualityFeatures f = which switch
        {
            "good" => Good(),
            "noisy" => Noisy(),
            "weak" => Weak(),
            _ => Unstable(),
        };
        var heuristic = new HeuristicSignalQualityClassifier();
        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();

        SignalQualityClass expected = heuristic.Classify(f).Class;
        SignalQualityAssessment got = model.Classify(f);

        // If the class-order sidecar were misread, the classes would be permuted
        // and this would fail.
        Assert.Equal(expected, got.Class);
        Assert.InRange(got.Confidence, 0f, 1f);
    }

    [Fact]
    public void Classify_IsDeterministic()
    {
        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();

        SignalQualityAssessment a = model.Classify(Noisy());
        SignalQualityAssessment b = model.Classify(Noisy());

        Assert.Equal(a.Class, b.Class);
        Assert.Equal(a.Confidence, b.Confidence);
    }

    [Fact]
    public void Classify_PreSync_ReturnsUnknown()
    {
        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();

        SignalQualityFeatures warming = Good() with { SyncedFraction = 0.2f };
        Assert.Equal(SignalQualityClass.Unknown, model.Classify(warming).Class);
    }

    // --- End-to-end: the ONNX model on real synth streams, mirroring
    // DetectorMetricsEngineQualityTests but with the trained classifier injected.

    private const int Bph = 28800;
    private const int SampleRate = 48000;

    private static SignalQualityClass FinalClassFor(WatchSynthStreamConfig cfg, ISignalQualityClassifier classifier, int seconds = 12)
    {
        var config = new DetectorMetricsEngineConfig(
            SampleRate: (int)cfg.SampleRateHz, LiftAngle: cfg.LiftAngleDegrees, AveragingPeriod: 2,
            UseCOnset: false, AutoBph: true, ManualBph: 0, HpfCutoffHz: 0.0);
        var engine = new DetectorMetricsEngine(config, classifier);
        var synth = new WatchSynthStream(cfg);

        float[] block = new float[4096];
        int remaining = (int)cfg.SampleRateHz * seconds;
        DetectorMetricsBlockUpdate last = engine.Flush();
        while (remaining > 0)
        {
            int slice = Math.Min(block.Length, remaining);
            Span<float> span = block.AsSpan(0, slice);
            synth.Generate(span);
            last = engine.Process(span);
            remaining -= slice;
        }
        last = engine.Flush();
        return last.Result.QualityAssessment?.Class ?? SignalQualityClass.Unknown;
    }

    private static WatchSynthStreamConfig Base()
    {
        WatchSynthStreamConfig cfg = WatchSynthStreamConfig.Clean();
        cfg.SampleRateHz = SampleRate;
        cfg.Bph = Bph;
        return cfg;
    }

    [Fact]
    public void CleanStream_ClassifiedGood_EndToEnd()
    {
        WatchSynthStreamConfig cfg = Base();
        cfg.PcmPeakSignalLevel = 0.40;
        cfg.NoisePeakSignalLevel = 0.0;

        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();
        Assert.Equal(SignalQualityClass.Good, FinalClassFor(cfg, model));
    }

    [Fact]
    public void UnstableStream_ClassifiedUnstable_EndToEnd()
    {
        WatchSynthStreamConfig cfg = Base();
        cfg.TimingJitterUs = 10000.0;

        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();
        Assert.Equal(SignalQualityClass.Unstable, FinalClassFor(cfg, model));
    }

    [Fact]
    public void NoiseDominatedStream_FlaggedDegraded_EndToEnd()
    {
        // Membership rather than a single class: by the time SNR drops into the
        // weak band timing also destabilises, so Unstable/Noisy/Weak are all valid
        // degraded verdicts. The point is it must NOT read Good or Unknown.
        WatchSynthStreamConfig cfg = Base();
        cfg.PcmPeakSignalLevel = 0.30;
        cfg.NoisePeakSignalLevel = 0.135;

        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();
        SignalQualityClass cls = FinalClassFor(cfg, model);

        Assert.NotEqual(SignalQualityClass.Good, cls);
        Assert.NotEqual(SignalQualityClass.Unknown, cls);
    }

    [Fact]
    public void OnnxAndHeuristic_AgreeOnCleanStream_EndToEnd()
    {
        WatchSynthStreamConfig onnxCfg = Base();
        onnxCfg.PcmPeakSignalLevel = 0.40;
        WatchSynthStreamConfig heuristicCfg = Base();
        heuristicCfg.PcmPeakSignalLevel = 0.40;

        using OnnxSignalQualityClassifier model = OnnxSignalQualityClassifier.LoadDefault();
        SignalQualityClass onnx = FinalClassFor(onnxCfg, model);
        SignalQualityClass heuristic = FinalClassFor(heuristicCfg, new HeuristicSignalQualityClassifier());

        Assert.Equal(heuristic, onnx);
    }

    // --- Composition-root fallback (explicitly-requested graceful degradation).

    [Fact]
    public void LoadOrElse_ReturnsFallback_WhenPrimaryThrows()
    {
        var fallback = new HeuristicSignalQualityClassifier();

        ISignalQualityClassifier chosen = OnnxSignalQualityClassifier.LoadOrElse(
            () => throw new InvalidOperationException("model missing"),
            () => fallback);

        Assert.Same(fallback, chosen);
    }

    [Fact]
    public void LoadOrElse_ReturnsPrimary_WhenItSucceeds()
    {
        var primary = new HeuristicSignalQualityClassifier();
        var fallback = new HeuristicSignalQualityClassifier();

        ISignalQualityClassifier chosen = OnnxSignalQualityClassifier.LoadOrElse(() => primary, () => fallback);

        Assert.Same(primary, chosen);
    }

    [Fact]
    public void Constructor_Throws_OnUnreadableModel()
    {
        // InferenceSession surfaces a malformed model as a typed OnnxRuntimeException;
        // pin the exact type so a future silent swallow (returning a broken session)
        // cannot pass as "threw something".
        Assert.Throws<Microsoft.ML.OnnxRuntime.OnnxRuntimeException>(() =>
            new OnnxSignalQualityClassifier(new byte[] { 0, 1, 2, 3 }, new[] { "Good", "Noisy" }));
    }
}
