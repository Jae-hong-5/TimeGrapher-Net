using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Pure summary math over decimated history series. Buckets in a
/// MetricsHistorySeries are uniform-size by construction (DecimatingSeries keeps
/// them uniform across compactions), so the plain mean of stored points equals
/// the mean of the underlying per-beat values.
/// </summary>
internal static class MetricsSeriesMath
{
    /// <summary>Average of all stored points (since measurement start). Null when empty.</summary>
    public static double? Average(MetricsHistorySeries series)
    {
        if (series.Y.Count == 0)
        {
            return null;
        }

        double sum = 0.0;
        for (int i = 0; i < series.Y.Count; i++)
        {
            sum += series.Y[i];
        }

        return sum / series.Y.Count;
    }

    /// <summary>
    /// Rolling average over the trailing window (seconds of stream time, measured
    /// back from the newest point). Null when the series is empty.
    /// </summary>
    public static double? RollingAverage(MetricsHistorySeries series, double windowS)
    {
        int count = series.Y.Count;
        if (count == 0)
        {
            return null;
        }

        double cutoff = series.X[count - 1] - windowS;
        double sum = 0.0;
        int n = 0;
        for (int i = count - 1; i >= 0 && series.X[i] >= cutoff; i--)
        {
            sum += series.Y[i];
            n++;
        }

        return n > 0 ? sum / n : null;
    }
}
