using System.Globalization;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure summary logic behind the Long-Term Performance footer, kept out of the
/// renderer so it is unit-testable without a live plot control: overall averages
/// for the testing period, elapsed time, and the current plot resolution. The
/// resolution readout ("1 pt ≈ N s") surfaces the plan's reduced-update-frequency
/// requirement: DecimatingSeries merges bucket pairs whenever its fixed capacity
/// fills, so one plotted point inherently spans more seconds as the run grows —
/// no explicit refresh-rate switching exists to report, only the bucket width.
/// </summary>
internal static class LongTermReadout
{
    /// <summary>
    /// Median spacing (s) between consecutive stored points — the stream time one
    /// plotted point currently represents. Median rather than mean so a few
    /// sync-loss gaps do not inflate the reading. Null until two points exist.
    /// </summary>
    public static double? MedianPointSpacingS(MetricsHistorySeries series)
    {
        int count = series.X.Count;
        if (count < 2)
        {
            return null;
        }

        var spacings = new double[count - 1];
        for (int i = 1; i < count; i++)
        {
            spacings[i - 1] = series.X[i] - series.X[i - 1];
        }

        Array.Sort(spacings);
        int mid = spacings.Length / 2;
        return (spacings.Length & 1) == 1
            ? spacings[mid]
            : (spacings[mid - 1] + spacings[mid]) / 2.0;
    }

    /// <summary>Current plot resolution, e.g. "1 pt ≈ 2.0 s" (em dash while unknown).</summary>
    public static string FormatResolution(double? secondsPerPoint) =>
        secondsPerPoint is double s
            ? "1 pt ≈ " + s.ToString(s < 10.0 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s"
            : "1 pt ≈ " + VarioReadout.Missing;

    /// <summary>
    /// Footer line: overall averages for the testing period (rate / amplitude /
    /// beat error), elapsed time, and the current resolution (taken from the
    /// rate series — the densest per-beat history).
    /// </summary>
    public static string Footer(BeatMetricsHistorySnapshot history) =>
        string.Join("   ",
            "AVG RATE " + VarioReadout.Format(
                MetricsSeriesMath.Average(history.Rate), "+0.0;-0.0;0.0", " s/d"),
            "AVG AMP " + VarioReadout.Format(
                MetricsSeriesMath.Average(history.Amplitude), "0", "°"),
            "AVG BEAT ERR " + VarioReadout.Format(
                MetricsSeriesMath.Average(history.BeatError), "+0.00;-0.00;0.00", " ms"),
            "ELAPSED " + VarioReadout.FormatElapsed(history.LatestTimeS),
            FormatResolution(MedianPointSpacingS(history.Rate)));
}
