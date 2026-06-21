using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal readonly record struct TraceAlertState(bool RateSlow, bool AmplitudeOutOfRange, string? Message);

/// <summary>
/// Trace-display alert policy from the project plan: alert when the watch runs
/// late (losing time) and when amplitude leaves its normal range (default
/// 270-315°, now read live from the shared <see cref="AcceptBandSettings"/> so the
/// band is user-editable). Pure so the thresholds are unit-testable; evaluated
/// against the same snapshot the graphs render, which keeps numbers and visuals
/// consistent by construction. The late-running threshold below is an asymmetric
/// alert gate, deliberately separate from the editable amplitude band.
/// </summary>
internal static class TraceAlertEvaluator
{
    // The amplitude normal band: the single source for every amplitude display.
    // Read live from AcceptBandSettings.Current so a Settings-window edit reaches
    // this evaluator, the Vario gauge and the Trace/Long-Term bands at once.
    public static double AmplitudeMinDeg => AcceptBandSettings.Current.AmplitudeMinDeg;
    public static double AmplitudeMaxDeg => AcceptBandSettings.Current.AmplitudeMaxDeg;

    // Small deadband below zero so the banner does not flicker on a healthy
    // watch hovering around on-time.
    public const double RateSlowThresholdSPerDay = -1.0;

    public static TraceAlertState Evaluate(BeatMetricsHistorySnapshot snapshot)
    {
        bool rateSlow = snapshot.RateValid && snapshot.RateSPerDay < RateSlowThresholdSPerDay;
        bool amplitudeOut = snapshot.AmplitudeValid &&
                            (snapshot.AmplitudeDeg < AmplitudeMinDeg || snapshot.AmplitudeDeg > AmplitudeMaxDeg);

        string? message = null;
        if (rateSlow && amplitudeOut)
        {
            message = FormatRate(snapshot.RateSPerDay) + "  |  " + FormatAmplitude(snapshot.AmplitudeDeg);
        }
        else if (rateSlow)
        {
            message = FormatRate(snapshot.RateSPerDay);
        }
        else if (amplitudeOut)
        {
            message = FormatAmplitude(snapshot.AmplitudeDeg);
        }

        return new TraceAlertState(rateSlow, amplitudeOut, message);
    }

    private static string FormatRate(double rateSPerDay) => string.Format(
        CultureInfo.InvariantCulture, "Watch is running late ({0:+0.0;-0.0} s/d)", rateSPerDay);

    private static string FormatAmplitude(double amplitudeDeg) => string.Format(
        CultureInfo.InvariantCulture, "Amplitude {0:F0}° outside normal range {1:F0}–{2:F0}°",
        amplitudeDeg, AmplitudeMinDeg, AmplitudeMaxDeg);
}
