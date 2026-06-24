namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// Re-times (does not veto) the A/C landmarks of a locked beat at the metrics
/// choke point. Where <see cref="IBeatEventGate"/> can only DROP an event, a
/// refiner proposes a corrected C (and optionally A) PEAK sample for the
/// metrics/display stream - targeting the observed failure where the C peak
/// latches a neighbouring ringing lobe instead of the true C.
///
/// <para>Structural guarantees (policy lives in the host, not in
/// implementations):</para>
/// <list type="bullet">
/// <item>BPH detection and the sync PLL always see the RAW detector stream;
/// the refiner sits strictly between detection and the metrics/display
/// consumers, so it cannot break lock acquisition.</item>
/// <item>The host clamps every correction to a small window around the
/// detector candidate and falls back to the detector value when confidence is
/// low, so a wrong or overconfident model cannot move a landmark far.</item>
/// <item>The refiner corrects the C/A PEAK only. C-onset timing
/// (<c>UseCOnset</c>) is re-derived deterministically from the corrected peak
/// downstream, so the refiner never needs to know the placement toggle and the
/// two cannot double-correct.</item>
/// </list>
///
/// <para>This is the TinyML socket for landmark correction (parallel to the
/// gate's classifier socket): a future ONNX model in a leaf inference project
/// implements this interface and is injected at the composition root, keeping
/// Core dependency-free.</para>
/// </summary>
public interface IBeatLandmarkRefiner
{
    /// <summary>Short identifier used by the verifier's reporting.</summary>
    string Name { get; }

    /// <summary>
    /// Requested envelope context around the beat, in milliseconds. When both
    /// are zero the refiner is called with an empty window and adds zero
    /// latency; otherwise the host buffers the delayed envelope and calls
    /// <see cref="Refine"/> once the post-window is available (post-window plus
    /// at most one analysis block later; event timestamps are unaffected).
    /// </summary>
    double WindowPreMs { get; }
    double WindowPostMs { get; }

    /// <summary>
    /// Returns a corrected-C/A proposal for the beat, or
    /// <see cref="BeatLandmarkRefinement.Fallback"/> to keep the detector
    /// values. <paramref name="envelopeWindow"/> is a slice of the delayed
    /// envelope (ProcessedPcm domain); it can be shorter than requested near
    /// stream boundaries. <paramref name="aOffsetInWindow"/> /
    /// <paramref name="cOffsetInWindow"/> are the detector A/C peak positions
    /// within that slice, or -1 when no window was requested or the position
    /// falls outside the available slice.
    /// </summary>
    BeatLandmarkRefinement Refine(
        ReadOnlySpan<float> envelopeWindow,
        int aOffsetInWindow,
        int cOffsetInWindow,
        double sampleRate,
        in BeatLandmarkCandidate candidate);

    /// <summary>Called on sync loss and detector regime reset.</summary>
    void Reset();
}
