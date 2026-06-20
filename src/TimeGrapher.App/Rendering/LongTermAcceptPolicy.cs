namespace TimeGrapher.App.Rendering;

/// <summary>
/// Acceptable-range bounds drawn as a shaded tolerance band with numeric limit
/// labels on the Long-Term Performance Graph (and the Trace display), one
/// (Min, Max) corridor per measure. The bounds
/// are NOT defined here: they alias the bands the other displays already use —
/// rate from <see cref="VarioGaugePolicy"/>, amplitude from
/// <see cref="TraceAlertEvaluator"/>, and beat error from
/// <see cref="BeatErrorDiagnostics"/> — so every view judges "in tolerance"
/// against the same numbers (the project's consistency driver, QAS-4). Pure, so
/// the aliasing is unit-testable without a live plot.
/// </summary>
internal static class LongTermAcceptPolicy
{
    /// <summary>Rate corridor (s/d): the project's healthy-watch band, shared with the Vario gauge.</summary>
    public static (double Min, double Max) Rate =>
        (VarioGaugePolicy.RateAcceptMinSPerDay, VarioGaugePolicy.RateAcceptMaxSPerDay);

    /// <summary>Amplitude corridor (°): the plan's 270–300° normal range the Trace display alerts on.</summary>
    public static (double Min, double Max) Amplitude =>
        (VarioGaugePolicy.AmplitudeAcceptMinDeg, VarioGaugePolicy.AmplitudeAcceptMaxDeg);

    /// <summary>
    /// Beat-error corridor (ms): symmetric about zero at the same magnitude the
    /// Beat Error diagnostic flags, since the pane plots the signed value.
    /// </summary>
    public static (double Min, double Max) BeatError =>
        (-BeatErrorDiagnostics.SeparationAlertThresholdMs, BeatErrorDiagnostics.SeparationAlertThresholdMs);
}
