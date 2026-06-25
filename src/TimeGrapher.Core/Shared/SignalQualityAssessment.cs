namespace TimeGrapher.Core.Shared;

/// <summary>
/// The classifier's verdict for the current window: a <see cref="SignalQualityClass"/>,
/// the confidence in that class (0..1), and the features it was derived from.
///
/// Attached to the per-block detector snapshot and carried on the AnalysisFrame
/// as an annotation; it never participates in the event-drop path. User-facing
/// guidance text and localisation live in the App layer, keyed off
/// <see cref="Class"/>, so Core stays presentation-free. A pure DTO, so it lives
/// in Core.Shared.
/// </summary>
/// <param name="Class">The coarse quality verdict.</param>
/// <param name="Confidence">Confidence in <paramref name="Class"/>, in [0, 1].</param>
/// <param name="Features">The feature vector the verdict was computed from.</param>
public readonly record struct SignalQualityAssessment(
    SignalQualityClass Class,
    float Confidence,
    SignalQualityFeatures Features)
{
    /// <summary>The not-yet-assessed assessment (pre-sync / warming up).</summary>
    public static SignalQualityAssessment Unknown { get; } =
        new(SignalQualityClass.Unknown, 0f, default);
}
