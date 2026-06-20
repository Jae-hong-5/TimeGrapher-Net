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
}
