using System;
using System.IO;
using TimeGrapher.Core.Detection.Scoring;
using TimeGrapher.Inference;
using Xunit;

namespace TimeGrapher.Verify.Tests;

/// <summary>
/// Tests for the ONNX refiner's model-independent surface: the pure output
/// decode (window-relative offsets -> absolute corrected A/C) and the
/// missing-model guard. The inference path itself is exercised only when a
/// trained .onnx model is supplied via --landmark=onnx:&lt;path&gt;.
/// </summary>
public sealed class OnnxBeatLandmarkRefinerTests
{
    private static BeatLandmarkCandidate Candidate(double aSample, double cSample) => new(
        AEvent: default, CEvent: default, ASample: aSample, CSample: cSample,
        Synced: true, DetectedBph: 21600, BeatPeriodS: 1.0 / 6.0,
        NoiseFloor: 0.0f, ReferencePeak: 1.0f);

    [Fact]
    public void MapOutputToRefinement_ConvertsWindowOffsetsToAbsolute()
    {
        // Window starts at ASample - aOffsetInWindow = 1000 - 288 = 712.
        BeatLandmarkCandidate candidate = Candidate(aSample: 1000.0, cSample: 1400.0);
        float[] output = { 290f, 690f, 0.9f, 0.8f }; // a_off, c_off, a_conf, c_conf

        BeatLandmarkRefinement r = OnnxBeatLandmarkRefiner.MapOutputToRefinement(output, candidate, aOffsetInWindow: 288);

        Assert.True(r.Accepted);
        Assert.True(r.CorrectedA);
        Assert.True(r.CorrectedC);
        Assert.Equal(712.0 + 290.0, r.CorrectedASample);
        Assert.Equal(712.0 + 690.0, r.CorrectedCSample);
        Assert.Equal(0.9f, r.AConfidence);
        Assert.Equal(0.8f, r.CConfidence);
    }

    [Fact]
    public void MapOutputToRefinement_DefaultsConfidenceWhenOutputIsShort()
    {
        BeatLandmarkRefinement r = OnnxBeatLandmarkRefiner.MapOutputToRefinement(
            new float[] { 10f, 20f }, Candidate(0.0, 0.0), aOffsetInWindow: 0);

        Assert.Equal(1.0f, r.AConfidence);
        Assert.Equal(1.0f, r.CConfidence);
        Assert.Equal(10.0, r.CorrectedASample);
        Assert.Equal(20.0, r.CorrectedCSample);
    }

    [Fact]
    public void Constructor_ThrowsWhenModelMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no-such-model-" + Guid.NewGuid().ToString("N") + ".onnx");
        Assert.Throws<FileNotFoundException>(() => new OnnxBeatLandmarkRefiner(missing));
    }
}
