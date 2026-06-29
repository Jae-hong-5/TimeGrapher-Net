using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The shared pause-and-review scrub cursor. It is a ScottPlot VerticalLine (so it
/// spans the viewport without contributing a Y extent to autoscaling): Update(x)
/// shows it at x with autoscale disabled, and Update(null) hides it.
/// </summary>
public sealed class ReviewCursorLayerTests
{
    private static ReviewCursorLayer NewLayer(out AvaPlot plot)
    {
        plot = new AvaPlot();
        return new ReviewCursorLayer(plot.Plot);
    }

    [Fact]
    public void Update_ShowsCursorAtX_WithoutContributingToAutoscale()
    {
        ReviewCursorLayer layer = NewLayer(out AvaPlot plot);

        Assert.True(layer.Update(5.0));

        VerticalLine line = Assert.Single(plot.Plot.GetPlottables<VerticalLine>());
        Assert.True(line.IsVisible);
        Assert.Equal(5.0, line.X);
        Assert.False(line.EnableAutoscale);
    }

    [Fact]
    public void Update_Null_HidesCursor()
    {
        ReviewCursorLayer layer = NewLayer(out AvaPlot plot);
        layer.Update(5.0);

        Assert.True(layer.Update(null));

        VerticalLine line = Assert.Single(plot.Plot.GetPlottables<VerticalLine>());
        Assert.False(line.IsVisible);
    }
}
