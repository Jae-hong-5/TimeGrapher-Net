namespace TimeGrapher.Core.Detection.Scoring;

/// <summary>
/// One beat's A/C landmark pair plus the context it was detected under, handed
/// to an <see cref="IBeatLandmarkRefiner"/>. Unlike <see cref="BeatCandidate"/>
/// (a single event for the veto-only gate), a refiner reasons about the A and
/// C of the same beat together, because the failure it targets is the C peak
/// latching the wrong lobe relative to its own A.
///
/// <para><see cref="ASample"/> / <see cref="CSample"/> are the detector's
/// chosen samples in the ProcessedPcm (delayed-envelope) domain - the same
/// quantity the metrics consume - so a refiner's corrections are directly
/// comparable to them. They carry the C PEAK sample (the detector primitive);
/// onset timing is re-derived downstream from the corrected peak, never fed in
/// here (see <see cref="IBeatLandmarkRefiner"/>).</para>
/// </summary>
public readonly record struct BeatLandmarkCandidate(
    TgEvent AEvent,
    TgEvent CEvent,
    double ASample,
    double CSample,
    bool Synced,
    int DetectedBph,
    double BeatPeriodS,
    float NoiseFloor,
    float ReferencePeak);
