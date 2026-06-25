using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The Rate/Scope renderer accumulates the producer's latest-window slices into a
/// rolling history (so a pan/pause can reach back without the producer re-copying the
/// whole retention) and reduces only the visible X range for display.
/// </summary>
public sealed class RateScopeHistoryTests
{
    [Theory]
    [InlineData(0.0, 0.0, 120.0)]
    [InlineData(119.999, 0.0, 120.0)]
    [InlineData(120.0, 120.0, 240.0)]
    [InlineData(239.999, 120.0, 240.0)]
    [InlineData(240.0, 240.0, 360.0)]
    public void RatePageWindowFor_AdvancesInFixedPages(double maxBeat, double expectedLeft, double expectedRight)
    {
        (double left, double right) = RateScopeRenderer.RatePageWindowFor(maxBeat);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedRight, right);
    }

    [Fact]
    public void RenderFrame_ShowsAveragePeriodRateIntervalOverlay()
    {
        var scopePlot = new AvaPlot();
        var ratePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, ratePlot, "Arial");
        renderer.CreateGraphs(rateErrorYScale: 10.0, rateDataPoints: 600);

        var frame = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                AveragePeriodRateIntervals = new[]
                {
                    new AveragePeriodRateInterval(0.0, 4.0, 0.0, 3.0, 1728.0),
                },
            },
        };
        AddRateSeries(frame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 0.0, 4.0 },
            Y = new[] { 0.0, 1.0 },
            Replace = true,
        });

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));

        HorizontalSpan span = Assert.Single(ratePlot.Plot.GetPlottables<HorizontalSpan>(), s => s.IsVisible);
        Assert.Equal(0.0, span.X1);
        Assert.Equal(4.0, span.X2);
        Text label = Assert.Single(ratePlot.Plot.GetPlottables<Text>(), t => t.IsVisible);
        Assert.Equal("+1728.0 s/d", label.LabelText);
    }

    private static void AddRateSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddRateSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }

    [Fact]
    public void MergeScopeSlice_AppendsAndIsIdempotentOnReplay()
    {
        var hx = new List<double>();
        var hy = new List<double>();

        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0 }, new[] { 0.0, 10.0, 20.0 }, retentionSamples: 1000);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, hy);

        // The throttled producer re-attaches the same slice between rebuilds: merging
        // it again must not duplicate or shift anything.
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0 }, new[] { 0.0, 10.0, 20.0 }, retentionSamples: 1000);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, hy);
    }

    [Fact]
    public void MergeScopeSlice_OverlapSupersedesTailThenExtends()
    {
        var hx = new List<double>();
        var hy = new List<double>();
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0, 3.0, 4.0 }, new[] { 0.0, 10.0, 20.0, 30.0, 40.0 }, 1000);

        // A fresh slice starting at X=3 supersedes the overlapping tail (3,4) with its
        // newer Y values and extends the history to 6.
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 3.0, 4.0, 5.0, 6.0 }, new[] { 300.0, 400.0, 500.0, 600.0 }, 1000);

        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0, 300.0, 400.0, 500.0, 600.0 }, hy);
    }

    [Fact]
    public void MergeScopeSlice_TrimsFrontToRetention()
    {
        var hx = new List<double>();
        var hy = new List<double>();

        // newest = 10, retention 5 -> keep only X >= 10 - 5 = 5.
        RateScopeRenderer.MergeScopeSlice(
            hx, hy,
            new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 },
            new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 },
            retentionSamples: 5);

        Assert.Equal(new[] { 6.0, 8.0, 10.0 }, hx);
        Assert.Equal(new[] { 6.0, 8.0, 10.0 }, hy);
    }

    [Fact]
    public void ReduceRangeTo_KeepsVisibleRangePlusOneNeighbourEachSide()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 3.0, right: 6.0, targetPointBudget: 100, tx, ty);

        // Visible [3,6] plus the neighbour just left (2) so the line reaches the edge.
        Assert.Equal(new[] { 2.0, 3.0, 4.0, 5.0, 6.0 }, tx);
        Assert.Equal(new[] { 20.0, 30.0, 40.0, 50.0, 60.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_IncludesNeighbourJustRightOfViewEdge()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        // Non-exact right edge (6.5): the reduction must include 7.0, the point just
        // right of the view, so the drawn line reaches the edge. (right: 6.0 lands on a
        // sample, so it never exercises the right-neighbour inclusion.)
        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 3.0, right: 6.5, targetPointBudget: 100, tx, ty);

        Assert.Equal(new[] { 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 }, tx);
        Assert.Equal(new[] { 20.0, 30.0, 40.0, 50.0, 60.0, 70.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_SubsamplesToBudget()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 0.0, right: 9.0, targetPointBudget: 3, tx, ty);

        // 10 points over budget 3 -> stride 4 (matches SeriesDataReducer).
        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, tx);
        Assert.Equal(new[] { 0.0, 40.0, 80.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_EmptySourceProducesEmptyTarget()
    {
        var tx = new List<double> { 1.0 };
        var ty = new List<double> { 1.0 };

        RateScopeRenderer.ReduceRangeTo(new List<double>(), new List<double>(), left: 0.0, right: 1.0, targetPointBudget: 10, tx, ty);

        Assert.Empty(tx);
        Assert.Empty(ty);
    }
}
