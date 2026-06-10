using ScottPlot;
using ScottPlot.Plottables;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the ScottPlot behaviors the review-cursor design relies on. If a
/// ScottPlot upgrade changes these, the cursor implementation must be
/// re-validated (review finding: a cursor LinePlot spanning ±1e6 blew the
/// autoscaled Y axis of every plot it was added to).
/// </summary>
public sealed class ScottPlotAutoScaleBehaviorTests
{
    [Fact]
    public void AutoScale_IncludesVisibleLinePlotExtent()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 0, 1 }, new double[] { 0.0, 0.5 });

        LinePlot cursor = plot.Add.Line(0.5, -1e6, 0.5, 1e6);
        cursor.IsVisible = true;

        plot.Axes.AutoScale();
        AxisLimits limits = plot.Axes.GetLimits();

        // Documents the hazard the cursor fix removes: a visible ±1e6 line
        // (the old scrub-cursor shape) blows the autoscaled Y fit.
        Assert.True(limits.Top > 1000.0, $"expected blown Y axis, got {limits.Top}");
    }

    [Fact]
    public void AutoScale_ExcludesInvisiblePlottables()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 0, 1 }, new double[] { 0.0, 0.5 });

        LinePlot cursor = plot.Add.Line(0.5, -1e6, 0.5, 1e6);
        cursor.IsVisible = false;

        plot.Axes.AutoScale();
        AxisLimits limits = plot.Axes.GetLimits();

        // Hidden plottables do not contribute (the pooled-marker pattern in
        // RateScopeRenderer relies on this too).
        Assert.InRange(limits.Top, 0.4, 1.0);
    }

    [Fact]
    public void AutoScale_IgnoresVerticalLineYExtent()
    {
        var plot = new Plot();
        plot.Add.Scatter(new double[] { 0, 1 }, new double[] { 0.0, 0.5 });

        VerticalLine cursor = plot.Add.VerticalLine(0.5);
        _ = cursor;

        plot.Axes.AutoScale();
        AxisLimits limits = plot.Axes.GetLimits();

        // An AxisLine spans the viewport at render time without contributing a
        // Y extent, so it is safe to keep on autoscaled plots.
        Assert.InRange(limits.Top, 0.4, 1.0);
        Assert.InRange(limits.Bottom, -0.5, 0.1);
    }
}
