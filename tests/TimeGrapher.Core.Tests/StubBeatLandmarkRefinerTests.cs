using TimeGrapher.Core.Detection.Scoring;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// Unit tests for the deterministic stub refiner: CPeak snaps C to the local
/// envelope maximum (expressed as an absolute sample relative to the detector
/// candidate), NoOp and the no-window sentinels fall back.
/// </summary>
public sealed class StubBeatLandmarkRefinerTests
{
    private static BeatLandmarkCandidate Candidate(double cSample) => new(
        AEvent: default, CEvent: default, ASample: 0.0, CSample: cSample,
        Synced: true, DetectedBph: 21600, BeatPeriodS: 1.0 / 6.0,
        NoiseFloor: 0.0f, ReferencePeak: 1.0f);

    [Fact]
    public void CPeak_SnapsCToTheLocalEnvelopeMaximum()
    {
        var stub = new StubBeatLandmarkRefiner(StubBeatLandmarkRefiner.Mode.CPeak);
        var window = new float[100];
        Array.Fill(window, 0.1f);
        window[60] = 1.0f; // a clear peak 20 samples after the detector C at offset 40

        BeatLandmarkRefinement r = stub.Refine(window, aOffsetInWindow: 10, cOffsetInWindow: 40,
                                               sampleRate: 48000, Candidate(cSample: 1000.0));

        Assert.True(r.Accepted);
        Assert.True(r.CorrectedC);
        Assert.Equal(1020.0, r.CorrectedCSample); // 1000 + (60 - 40)
        Assert.False(r.CorrectedA);
        Assert.True(r.CConfidence > 0.5f, $"confidence {r.CConfidence} should be high for a clear peak");
    }

    [Fact]
    public void NoOp_AlwaysFallsBack_AndRequestsNoWindow()
    {
        var stub = new StubBeatLandmarkRefiner(StubBeatLandmarkRefiner.Mode.NoOp);
        Assert.Equal(0.0, stub.WindowPreMs);
        Assert.Equal(0.0, stub.WindowPostMs);

        var window = new float[100];
        window[60] = 1.0f;
        BeatLandmarkRefinement r = stub.Refine(window, 10, 40, 48000, Candidate(1000.0));

        Assert.False(r.Accepted);
    }

    [Fact]
    public void CPeak_FallsBackWhenNoCWindowOffset()
    {
        var stub = new StubBeatLandmarkRefiner(StubBeatLandmarkRefiner.Mode.CPeak);

        BeatLandmarkRefinement empty = stub.Refine(
            ReadOnlySpan<float>.Empty, -1, -1, 48000, Candidate(1000.0));
        Assert.False(empty.Accepted);

        var window = new float[100];
        BeatLandmarkRefinement noCOffset = stub.Refine(window, 10, -1, 48000, Candidate(1000.0));
        Assert.False(noCOffset.Accepted);
    }
}
