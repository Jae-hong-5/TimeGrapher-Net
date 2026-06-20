using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal static class SeriesDataReducer
{
    /// <summary>First series with the given id, or null (the shared per-tab lookup).</summary>
    public static GraphSeriesFrame? FindSeries(IReadOnlyList<GraphSeriesFrame> seriesList, string id)
    {
        foreach (GraphSeriesFrame series in seriesList)
        {
            if (series.Id == id)
            {
                return series;
            }
        }

        return null;
    }

    public static bool TryReplaceSeriesData(
        GraphSeriesFrame? series,
        List<double> targetX,
        List<double> targetY,
        int targetPointBudget)
    {
        if (series == null)
        {
            return false;
        }

        if (!series.Replace)
        {
            throw new InvalidOperationException($"Graph series '{series.Id}' must be a replace snapshot.");
        }

        ReplaceSeriesData(targetX, targetY, series.X, series.Y, targetPointBudget);
        return true;
    }

    public static void ReplaceSeriesData(
        List<double> targetX,
        List<double> targetY,
        IReadOnlyList<double> sourceX,
        IReadOnlyList<double> sourceY,
        int targetPointBudget)
    {
        targetX.Clear();
        targetY.Clear();

        int count = Math.Min(sourceX.Count, sourceY.Count);
        if (count == 0)
        {
            return;
        }

        int stride = targetPointBudget > 0 && count > targetPointBudget
            ? (int)Math.Ceiling(count / (double)targetPointBudget)
            : 1;

        for (int i = 0; i < count; i += stride)
        {
            targetX.Add(sourceX[i]);
            targetY.Add(sourceY[i]);
        }
    }

    /// <summary>
    /// Replace-decimates by the peak (max) of each bin instead of subsampling, so
    /// a non-negative envelope survives the reduction (no aliased spikes). Every
    /// lane bins the same source positions, so the bin-start X is consistent
    /// across lanes — their time axes stay aligned. Returns false for a null series.
    /// </summary>
    public static bool TryReplaceSeriesDataPeak(
        GraphSeriesFrame? series,
        List<double> targetX,
        List<double> targetY,
        int targetPointBudget)
    {
        if (series == null)
        {
            return false;
        }

        if (!series.Replace)
        {
            throw new InvalidOperationException($"Graph series '{series.Id}' must be a replace snapshot.");
        }

        targetX.Clear();
        targetY.Clear();

        IReadOnlyList<double> sourceX = series.X;
        IReadOnlyList<double> sourceY = series.Y;
        int count = Math.Min(sourceX.Count, sourceY.Count);
        if (count == 0)
        {
            return true;
        }

        if (targetPointBudget <= 0 || count <= targetPointBudget)
        {
            for (int i = 0; i < count; i++)
            {
                targetX.Add(sourceX[i]);
                targetY.Add(sourceY[i]);
            }

            return true;
        }

        int binSize = (int)Math.Ceiling(count / (double)targetPointBudget);
        for (int start = 0; start < count; start += binSize)
        {
            int end = Math.Min(start + binSize, count);
            double peak = sourceY[start];
            for (int j = start + 1; j < end; j++)
            {
                if (sourceY[j] > peak)
                {
                    peak = sourceY[j];
                }
            }

            targetX.Add(sourceX[start]); // bin start: same positions for every lane
            targetY.Add(peak);
        }

        return true;
    }
}
