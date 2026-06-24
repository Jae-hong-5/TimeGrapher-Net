namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// A deterministic, dependency-free <see cref="IBeatLandmarkRefiner"/> for
/// tests, demos, and off-vs-refiner A/B comparison in the verifier when no
/// trained ONNX model is available. It is NOT a learned model and makes no
/// accuracy claim: in <see cref="Mode.CPeak"/> it re-times C to the largest
/// envelope sample within a search radius around the detector's C candidate
/// (the "snap C to the local envelope peak" heuristic), reporting a confidence
/// from how far that peak stands out above the local mean; in
/// <see cref="Mode.NoOp"/> it always falls back. The host still clamps every
/// correction and falls back below the confidence floor, so the stub cannot
/// move a landmark outside the safe window.
/// </summary>
public sealed class StubBeatLandmarkRefiner : IBeatLandmarkRefiner
{
    public enum Mode
    {
        /// <summary>Always falls back; requests no window (zero added latency).</summary>
        NoOp,

        /// <summary>Snaps C to the local envelope peak within the search radius.</summary>
        CPeak,
    }

    private readonly Mode _mode;
    private readonly double _searchRadiusMs;

    public StubBeatLandmarkRefiner(Mode mode = Mode.CPeak,
                                   double windowPreMs = 4.0,
                                   double windowPostMs = 8.0,
                                   double searchRadiusMs = 4.0)
    {
        _mode = mode;
        WindowPreMs = mode == Mode.NoOp ? 0.0 : windowPreMs;
        WindowPostMs = mode == Mode.NoOp ? 0.0 : windowPostMs;
        _searchRadiusMs = searchRadiusMs;
    }

    public string Name => _mode == Mode.NoOp ? "stub:noop" : "stub:cpeak";
    public double WindowPreMs { get; }
    public double WindowPostMs { get; }

    public BeatLandmarkRefinement Refine(ReadOnlySpan<float> envelopeWindow, int aOffsetInWindow,
                                         int cOffsetInWindow, double sampleRate, in BeatLandmarkCandidate candidate)
    {
        if (_mode == Mode.NoOp || cOffsetInWindow < 0 || envelopeWindow.IsEmpty)
        {
            return BeatLandmarkRefinement.Fallback;
        }

        int radius = (int)(_searchRadiusMs * 1e-3 * sampleRate);
        int lo = Math.Max(0, cOffsetInWindow - radius);
        int hi = Math.Min(envelopeWindow.Length - 1, cOffsetInWindow + radius);
        if (hi <= lo)
        {
            return BeatLandmarkRefinement.Fallback;
        }

        int argmax = cOffsetInWindow;
        float max = float.NegativeInfinity;
        double sum = 0.0;
        for (int i = lo; i <= hi; i++)
        {
            float v = Math.Abs(envelopeWindow[i]);
            sum += v;
            if (v > max)
            {
                max = v;
                argmax = i;
            }
        }

        // Confidence: how far the chosen peak stands out above the local mean.
        double mean = sum / (hi - lo + 1);
        float confidence = max > 0f ? (float)Math.Clamp((max - mean) / max, 0.0, 1.0) : 0.0f;

        double correctedCSample = candidate.CSample + (argmax - cOffsetInWindow);
        return new BeatLandmarkRefinement(
            Accepted: true, CorrectedC: true, CorrectedCSample: correctedCSample, CConfidence: confidence);
    }

    public void Reset()
    {
    }
}
