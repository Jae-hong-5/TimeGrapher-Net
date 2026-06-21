using System.Text.Json.Serialization;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Single mutable source of the acceptable ("normal") band limits every graph
/// judges against — Error Rate, Amplitude and Beat Error. The per-measure policy
/// classes (<see cref="TraceAlertEvaluator"/>, <see cref="VarioGaugePolicy"/>,
/// <see cref="BeatErrorDiagnostics"/>) read these values rather than holding their
/// own constants, so every display stays in tolerance against the same numbers by
/// construction (the project's consistency driver, QAS-4). Beat error is symmetric
/// about zero, so it is stored as a single magnitude. The defaults reproduce the
/// project plan's historical bands (rate ±10 s/d, amplitude 270–300°, beat error
/// ±0.6 ms) so behaviour is unchanged until the user edits them.
///
/// Thread confinement: <see cref="Current"/> is published once on startup (before
/// the UI thread runs) and thereafter read/written only on the UI thread (renderers
/// and the Settings handler); the analysis worker and Core never read it. It is a
/// whole-record swap, so it carries no per-field tearing risk.
/// </summary>
internal sealed record AcceptBandSettings(
    double RateMinSPerDay,
    double RateMaxSPerDay,
    double AmplitudeMinDeg,
    double AmplitudeMaxDeg,
    double BeatErrorMagnitudeMs)
{
    // Editable bounds — the same limits the SettingsWindow NumericUpDown controls
    // expose. IsValid enforces them so a hand-edited or corrupt JSON file cannot
    // load a band the UI cannot represent, nor an out-of-decimal-range / non-finite
    // value that would overflow the decimal cast the Settings inputs use.
    public const double RateLimitSPerDay = 120.0;
    public const double AmplitudeFloorDeg = 0.0;
    public const double AmplitudeCeilingDeg = 360.0;
    public const double BeatErrorFloorMs = 0.1;
    public const double BeatErrorCeilingMs = 10.0;

    public static AcceptBandSettings Default { get; } =
        new(-10.0, 10.0, 270.0, 300.0, 0.6);

    /// <summary>
    /// The live limits every graph reads; replaced (not mutated) when the user
    /// applies new values in the Settings window.
    /// </summary>
    public static AcceptBandSettings Current { get; set; } = Default;

    /// <summary>
    /// True when each measure's minimum is strictly below its maximum, every value
    /// is finite and within the editable bounds, and the beat-error magnitude is
    /// positive — the precondition for a drawable, UI-representable band. Comparisons
    /// against NaN/±∞ are false, so non-finite values are rejected. Not persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        RateMinSPerDay >= -RateLimitSPerDay && RateMaxSPerDay <= RateLimitSPerDay &&
        RateMinSPerDay < RateMaxSPerDay &&
        AmplitudeMinDeg >= AmplitudeFloorDeg && AmplitudeMaxDeg <= AmplitudeCeilingDeg &&
        AmplitudeMinDeg < AmplitudeMaxDeg &&
        BeatErrorMagnitudeMs >= BeatErrorFloorMs && BeatErrorMagnitudeMs <= BeatErrorCeilingMs;

    /// <summary>
    /// The apply-gate the Settings handler uses: true when <paramref name="candidate"/>
    /// is a valid band that actually differs from this one, so an incomplete (inverted)
    /// or no-op edit neither persists nor re-renders. Pure, so it is unit-testable
    /// without the window.
    /// </summary>
    public bool ShouldReplace(AcceptBandSettings candidate) =>
        candidate.IsValid && candidate != this;
}
