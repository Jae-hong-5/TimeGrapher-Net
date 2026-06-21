using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal enum BeatErrorDiagState
{
    Normal,
    SeparationAlert,
    MajorFault,
}

internal readonly record struct BeatErrorDiagnosis(BeatErrorDiagState State, string? Message);

/// <summary>
/// Beat Error Display diagnostic policy from the project plan, kept pure so the
/// thresholds are unit-testable. Two rules, evaluated against the same snapshot
/// the tab renders so numbers and visuals stay consistent by construction:
///
/// 1. Separation ALERT - when the two trace lines are shown, alert if their
///    separation exceeds an acceptable range. The tic/toc line separation IS the
///    signed beat error (each line is offset by half the tick-tock asymmetry),
///    so the rule fires on |BeatErrorSignedMs| above 0.6 ms - the plan's intro
///    calls a beat error under 0.6 ms good.
///
/// 2. MAJOR FAULT - "slope greater than 45 degrees in magnitude". A screen
///    angle is meaningless across axis scales, so the rule is pinned to DATA
///    units: the Error Rate trace plots milliseconds (y) against beat index (x),
///    where the per-beat drift in ms is
///        slope = rateSPerDay * (3600 / BPH) * 1000 / 86400
///    (seconds-per-day error, scaled to one beat period of 3600/BPH seconds,
///    in ms). The documented y=x 45-degree line at that scale is 1 ms per beat,
///    so the fault fires when |slope| exceeds 1.0 ms/beat.
/// </summary>
internal static class BeatErrorDiagnostics
{
    /// <summary>
    /// Acceptable tic/toc line separation (= signed beat error magnitude), ms.
    /// This IS the beat-error normal band the other displays draw (±this value),
    /// so it is read live from the shared AcceptBandSettings and a Settings-window
    /// edit moves both the alert threshold and the Long-Term band together.
    /// </summary>
    public static double SeparationAlertThresholdMs => AcceptBandSettings.Current.BeatErrorMagnitudeMs;

    /// <summary>The 45-degree (y=x) slope of the ms-vs-beat-index trace.</summary>
    public const double MajorFaultSlopeMsPerBeat = 1.0;

    /// <summary>Error Rate trace drift per beat in ms (0 when BPH is unknown).</summary>
    public static double SlopeMsPerBeat(double rateSPerDay, int bph) =>
        bph > 0 ? rateSPerDay * (3600.0 / bph) * 1000.0 / 86400.0 : 0.0;

    public static BeatErrorDiagnosis Evaluate(BeatMetricsHistorySnapshot snapshot)
    {
        // The fault outranks the alert: a >45-degree trace makes the line
        // separation a secondary concern.
        if (snapshot.RateValid && snapshot.Bph > 0)
        {
            double slope = SlopeMsPerBeat(snapshot.RateSPerDay, snapshot.Bph);
            if (Math.Abs(slope) > MajorFaultSlopeMsPerBeat)
            {
                return new BeatErrorDiagnosis(BeatErrorDiagState.MajorFault, string.Format(
                    CultureInfo.InvariantCulture,
                    "MAJOR FAULT: trace slope {0:+0.00;-0.00} ms/beat exceeds the 45° limit (±{1:0.0} ms/beat)",
                    slope, MajorFaultSlopeMsPerBeat));
            }
        }

        if (snapshot.BeatErrorValid && Math.Abs(snapshot.BeatErrorSignedMs) > SeparationAlertThresholdMs)
        {
            return new BeatErrorDiagnosis(BeatErrorDiagState.SeparationAlert, string.Format(
                CultureInfo.InvariantCulture,
                "Tic/toc separation {0:+0.00;-0.00} ms exceeds the acceptable ±{1:0.0} ms",
                snapshot.BeatErrorSignedMs, SeparationAlertThresholdMs));
        }

        return new BeatErrorDiagnosis(BeatErrorDiagState.Normal, null);
    }
}
