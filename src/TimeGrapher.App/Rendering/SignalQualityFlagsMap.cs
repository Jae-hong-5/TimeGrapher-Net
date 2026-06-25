using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// The single reconciliation point between the trained classifier's window-level
/// verdict (<see cref="SignalQualityAssessment"/>) and the per-beat rule flags
/// (<see cref="SignalQualityFlags"/>) the presentation layer already understands.
///
/// The model contributes only the degraded-health bits (Noisy / Weak / unstable
/// timing); the high-severity geometry bits (NoSignal / Clipping / PossibleFalseC)
/// stay the exclusive domain of the per-beat detector rules in BeatSegmentCapture,
/// so the two producers never overlap and the SignalQualityText priority ladder
/// stays coherent. A tentative (low-confidence) verdict maps to None so it never
/// raises a warning.
/// </summary>
internal static class SignalQualityFlagsMap
{
    /// <summary>Below this model confidence a verdict is treated as tentative and not surfaced.</summary>
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
