using ScottPlot;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
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

    [Fact]
    public void AutoScale_ExcludesVerticalLineXExtentOnlyWhenAutoscaleDisabled()
    {
        // A VerticalLine DOES contribute its X to autoscaling by default: a
        // paused-review cursor parked at an absolute stream time far outside
        // the visible window would stretch every ResetView / paused-scrub
        // AutoScale fit.
        var stretched = new Plot();
        stretched.Add.Scatter(new double[] { 0, 1 }, new double[] { 0.0, 0.5 });
        VerticalLine defaultCursor = stretched.Add.VerticalLine(100.0);
        _ = defaultCursor;
        stretched.Axes.AutoScale();
        Assert.True(
            stretched.Axes.GetLimits().Right > 50.0,
            $"expected blown X axis, got {stretched.Axes.GetLimits().Right}");

        // ReviewCursorLayer sets EnableAutoscale = false, which fully detaches
        // the cursor from the X fit — the load-bearing flag the cursor design
        // (and every ResetView with a visible cursor) relies on.
        var detached = new Plot();
        detached.Add.Scatter(new double[] { 0, 1 }, new double[] { 0.0, 0.5 });
        VerticalLine reviewCursor = detached.Add.VerticalLine(100.0);
        reviewCursor.EnableAutoscale = false;
        detached.Axes.AutoScale();
        AxisLimits limits = detached.Axes.GetLimits();
        Assert.InRange(limits.Right, 0.9, 2.0);
        Assert.InRange(limits.Left, -1.0, 0.1);
    }

    [Fact]
    public void ReviewCursorLayer_UsesRedBoldVerticalGuideStyle()
    {
        var plot = new Plot();
        var layer = new ReviewCursorLayer(plot);
        var palette = new PlotThemePalette(
            SurfaceBg: 0xFF000000,
            ScopeBg: 0xFF000000,
            ScopeGrid: 0xFF000000,
            TextPrimary: 0xFF000000,
            TraceWave: 0xFF000000,
            TraceTick: 0xFF000000,
            TraceTock: 0xFF000000,
            VarioBad: 0xFFCC2233);

        layer.ApplyTheme(palette);

        VerticalLine line = plot.GetPlottables<VerticalLine>().Single();
        Assert.Equal(GraphLinePatterns.VerticalGuide, line.LinePattern);
        Assert.Equal(2, line.LineWidth);
        Assert.Equal(Color.FromARGB(palette.VarioBad), line.Color);
    }
}
