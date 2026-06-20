using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure summary logic behind the Long-Term Performance readouts, kept out of the
/// renderer so it is unit-testable without a live plot control: the verdict, the
/// current per-measure summary values, and the review-cursor metrics.
/// </summary>
internal static class LongTermReadout
{
    /// <summary>Elapsed X-axis tick label: mm:ss for short runs, HH:mm for hour/day-scale views.</summary>
    public static string FormatElapsedTick(double seconds)
    {
        int total = Math.Max(0, (int)seconds);
        if (total >= 3600)
        {
            return $"{total / 3600:00}:{total % 3600 / 60:00}";
        }

        return $"{total / 60:00}:{total % 60:00}";
    }

    public static (double[] Values, string[] Labels) ElapsedTicks(double leftS, double rightS)
    {
        if (rightS <= leftS)
        {
            return (new[] { leftS }, new[] { FormatElapsedTick(leftS) });
        }

        double stepS = TickStepS(rightS - leftS);
        double first = Math.Ceiling(leftS / stepS) * stepS;
        var values = new List<double>();
        for (double value = first; value <= rightS + stepS * 0.001; value += stepS)
        {
            if (value >= leftS - stepS * 0.001)
            {
                values.Add(value);
            }
        }

        if (values.Count == 0)
        {
            values.Add(leftS);
        }

        return (values.ToArray(), values.Select(FormatElapsedTick).ToArray());
    }

    public static string Verdict(BeatMetricsHistorySnapshot history)
    {
        bool hasAnyReading =
            history.Rate.X.Count > 0 ||
            history.Amplitude.X.Count > 0 ||
            history.BeatError.X.Count > 0 ||
            history.RateValid ||
            history.AmplitudeValid ||
            history.BeatErrorValid;
        if (!hasAnyReading)
        {
            return "COLLECTING";
        }

        return SeriesOrCurrentInRange(
                   history.Rate,
                   history.RateValid,
                   history.RateSPerDay,
                   LongTermAcceptPolicy.Rate) &&
               SeriesOrCurrentInRange(
                   history.Amplitude,
                   history.AmplitudeValid,
                   history.AmplitudeDeg,
                   LongTermAcceptPolicy.Amplitude) &&
               SeriesOrCurrentInRange(
                   history.BeatError,
                   history.BeatErrorValid,
                   history.BeatErrorSignedMs,
                   LongTermAcceptPolicy.BeatError)
            ? "IN TOLERANCE"
            : "CHECK";
    }

    public static string CurrentRate(BeatMetricsHistorySnapshot history) =>
        "Error Rate " + VarioReadout.Format(history.RateValid ? history.RateSPerDay : null, "+0.0;-0.0;0.0", " s/d");

    public static string CurrentAmplitude(BeatMetricsHistorySnapshot history) =>
        "Amplitude " + VarioReadout.Format(history.AmplitudeValid ? history.AmplitudeDeg : null, "0", "°");

    public static string CurrentBeatError(BeatMetricsHistorySnapshot history) =>
        "BEAT ERROR " + VarioReadout.Format(history.BeatErrorValid ? history.BeatErrorSignedMs : null, "+0.0;-0.0;0.0", " ms");

    public static string ReviewMetrics(BeatMetricsHistorySnapshot history, double? cursorTimeS)
    {
        double? rate = cursorTimeS is double cursor
            ? VarioReadout.ValueAt(history.Rate, cursor)
            : history.RateValid ? history.RateSPerDay : null;
        double? amplitude = cursorTimeS is double cursorAmp
            ? VarioReadout.ValueAt(history.Amplitude, cursorAmp)
            : history.AmplitudeValid ? history.AmplitudeDeg : null;
        double? beatError = cursorTimeS is double cursorBeatError
            ? VarioReadout.ValueAt(history.BeatError, cursorBeatError)
            : history.BeatErrorValid ? history.BeatErrorSignedMs : null;

        return string.Join("   ",
            "Error Rate " + VarioReadout.Format(rate, "+0.0;-0.0;0.0", " s/d"),
            "Amplitude " + VarioReadout.Format(amplitude, "0", "°"),
            "BEAT ERROR " + VarioReadout.Format(beatError, "+0.0;-0.0;0.0", " ms"));
    }

    /// <summary>
    /// Beats right after a sequence reset are still settling — rate needs two
    /// beats, amplitude a swing pair — so a transient first-bucket spike must not
    /// flip the long-term verdict to CHECK. Buckets within this many seconds of a
    /// series' first stored point are excluded from the in-tolerance check; the
    /// settled live reading still counts when no later bucket exists yet.
    /// </summary>
    private const double WarmupExclusionS = 30.0;

    private static double TickStepS(double spanS) => spanS switch
    {
        <= 10.0 => 1.0,
        <= 30.0 => 5.0,
        <= 120.0 => 15.0,
        <= 10 * 60.0 => 60.0,
        <= 60 * 60.0 => 10 * 60.0,
        <= 6 * 60 * 60.0 => 60 * 60.0,
        _ => 6 * 60 * 60.0,
    };

    /// <summary>
    /// Judges a measure against its corridor over every stored bucket past the
    /// warmup window (see <see cref="WarmupExclusionS"/>). When only warmup
    /// buckets exist — or none at all — it falls back to the settled live reading,
    /// so a still-stabilizing run is never failed on its startup transient.
    /// </summary>
    private static bool SeriesOrCurrentInRange(
        MetricsHistorySeries series,
        bool currentValid,
        double current,
        (double Min, double Max) accept)
    {
        double warmupEnd = series.X.Count > 0 ? series.X[0] + WarmupExclusionS : 0.0;
        bool hasJudged = false;
        for (int i = 0; i < series.Y.Count; i++)
        {
            if (i < series.X.Count && series.X[i] < warmupEnd)
            {
                continue;
            }

            hasJudged = true;
            double low = i < series.YMin.Count ? series.YMin[i] : series.Y[i];
            double high = i < series.YMax.Count ? series.YMax[i] : series.Y[i];
            if (low < accept.Min || high > accept.Max)
            {
                return false;
            }
        }

        return hasJudged || !currentValid || (current >= accept.Min && current <= accept.Max);
    }
}
