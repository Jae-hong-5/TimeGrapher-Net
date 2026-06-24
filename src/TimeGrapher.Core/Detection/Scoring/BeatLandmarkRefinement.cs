namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// A refiner's verdict for one beat (see <see cref="IBeatLandmarkRefiner"/>).
///
/// <para>Corrections are expressed as absolute samples in the same
/// delayed-envelope domain as <see cref="BeatLandmarkCandidate.ASample"/> /
/// <see cref="BeatLandmarkCandidate.CSample"/>, and are always C/A PEAK
/// positions. The host - not the refiner - enforces the safety policy: it
/// clamps a correction to a small window around the original candidate and
/// falls back to the detector value when confidence is below threshold. A
/// refiner that emits an out-of-range or low-confidence result therefore
/// cannot move a landmark further than the clamp allows.</para>
///
/// <para><see cref="Accepted"/> = false means "no opinion - use the detector
/// values unchanged", the fail-open default that keeps a misbehaving or
/// uncertain model from degrading metrics. The per-landmark
/// <see cref="CorrectedC"/> / <see cref="CorrectedA"/> flags let a refiner
/// correct C only (the common case) while leaving A on the detector value.</para>
/// </summary>
public readonly record struct BeatLandmarkRefinement(
    bool Accepted,
    bool CorrectedC,
    double CorrectedCSample,
    float CConfidence,
    bool CorrectedA = false,
    double CorrectedASample = 0.0,
    float AConfidence = 0.0f)
{
    /// <summary>The fail-open verdict: keep the detector's A/C unchanged.</summary>
    public static BeatLandmarkRefinement Fallback => new(
        Accepted: false, CorrectedC: false, CorrectedCSample: 0.0, CConfidence: 0.0f);
}
