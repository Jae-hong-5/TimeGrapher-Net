namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// The default refiner: requests no window and always falls back, leaving the
/// detector's A/C untouched. With this installed (or no refiner configured at
/// all) the metrics/display stream is bit-identical to the un-refined pipeline,
/// which is the <c>landmark=off</c> acceptance baseline.
/// </summary>
public sealed class NoOpBeatLandmarkRefiner : IBeatLandmarkRefiner
{
    public string Name => "noop";
    public double WindowPreMs => 0.0;
    public double WindowPostMs => 0.0;

    public BeatLandmarkRefinement Refine(
        ReadOnlySpan<float> envelopeWindow,
        int aOffsetInWindow,
        int cOffsetInWindow,
        double sampleRate,
        in BeatLandmarkCandidate candidate)
        => BeatLandmarkRefinement.Fallback;

    public void Reset()
    {
    }
}
