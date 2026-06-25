using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// The single reconciliation point between the trained classifier's window-level
/// verdict (<see cref="SignalQualityAssessment"/>) and the per-beat rule flags
/// (<see cref="SignalQualityFlags"/>) the presentation layer already understands.
///
/// The model contributes only the degraded-health bits (Noisy / WeakSignal /
/// CTimingUnstable). The per-beat detector rules in BeatSegmentCapture can raise
/// those same bits too (by a different detection method — geometry rather than
/// window energy/timing), so the producers are NOT mutually exclusive there; the
/// bitwise-OR merge makes the overlap idempotent and harmless. What stays strictly
/// per-beat-exclusive are the high-severity geometry bits (NoSignal / Clipping /
/// PossibleFalseC), which the window model never raises — that keeps the
/// SignalQualityText priority ladder coherent. A tentative (low-confidence) verdict
/// maps to None so it never raises a warning.
/// </summary>
internal static class SignalQualityFlagsMap
{
    /// <summary>
    /// Below this model confidence a verdict is treated as tentative and not surfaced.
    /// Set below the heuristic fallback's minimum non-Unknown confidence (0.7 for
    /// Noisy) so the fallback always surfaces, while a future trained model's
    /// low-confidence outliers are suppressed rather than raising false warnings.
    /// </summary>
    public const float ConfidenceFloor = 0.5f;

    public static SignalQualityFlags From(SignalQualityAssessment? assessment)
    {
        if (assessment is not { } a || a.Confidence < ConfidenceFloor)
        {
            return SignalQualityFlags.None;
        }

        return a.Class switch
        {
            SignalQualityClass.Noisy => SignalQualityFlags.NoisySignal,
            SignalQualityClass.WeakSignal => SignalQualityFlags.WeakSignal,
            SignalQualityClass.Unstable => SignalQualityFlags.CTimingUnstable,
            _ => SignalQualityFlags.None, // Good / Unknown contribute no warning
        };
    }
}
