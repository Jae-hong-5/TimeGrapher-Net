using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure display policy for the Vario value gauges: the acceptable (green)
/// ranges and the gauge X-window derivation, kept free of UI types so both are
/// unit-testable. The rate and amplitude bands are read live from the shared
/// <see cref="AcceptBandSettings"/> (default rate ±10 s/d, amplitude 270–300°),
/// so a Settings-window edit moves the gauge band, the Trace/Long-Term bands and
/// the alerts together. Amplitude aliases <see cref="TraceAlertEvaluator"/> so the
/// two displays agree by construction.
/// </summary>
internal static class VarioGaugePolicy
{
    // The rate normal band, read live from the shared AcceptBandSettings so a
    // Settings-window edit reaches the gauge, the Trace and the Long-Term displays.
    public static double RateAcceptMinSPerDay => AcceptBandSettings.Current.RateMinSPerDay;
    public static double RateAcceptMaxSPerDay => AcceptBandSettings.Current.RateMaxSPerDay;

    // Amplitude still aliases TraceAlertEvaluator (now itself live), so the two
    // displays keep agreeing by construction.
    public static double AmplitudeAcceptMinDeg => TraceAlertEvaluator.AmplitudeMinDeg;
    public static double AmplitudeAcceptMaxDeg => TraceAlertEvaluator.AmplitudeMaxDeg;

    /// <summary>Fraction of the spanned width added on each side so edge markers stay visible.</summary>
    public const double GaugePaddingFraction = 0.05;

    /// <summary>
    /// Gauge X window: the acceptable range, widened to include the measured
    /// min/max and the current reading, then padded on both sides.
    /// </summary>
    public static (double Lo, double Hi) GaugeRange(
        double acceptMin, double acceptMax, StatsSummary stats, double? current)
    {
        double lo = acceptMin;
        double hi = acceptMax;

        if (stats.Valid)
        {
            lo = Math.Min(lo, stats.Min);
            hi = Math.Max(hi, stats.Max);
        }

        if (current is double value)
        {
            lo = Math.Min(lo, value);
            hi = Math.Max(hi, value);
        }

        double pad = (hi - lo) * GaugePaddingFraction;
        return (lo - pad, hi + pad);
    }

    public static bool ShouldShowAcceptBand(StatsSummary stats, double? current) =>
        stats.Valid || current.HasValue;
}
