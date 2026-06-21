using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the Sweep tab marker coloring: the A escapement markers use the green
/// tick color and the C markers the red tock color (colored by event type, not
/// by tic/toc phase). A markers are dashed and C markers dotted, so the line
/// pattern selects which set each assertion checks; the dotted review cursor is
/// excluded by its distinct color.
/// </summary>
public sealed class ScopeSweepRendererThemeTests
{
    private static PlotThemePalette Palette(uint green, uint red, uint cursor) => new(
        SurfaceBg: 0xFF101010,
        ScopeBg: 0xFF202020,
        ScopeGrid: 0xFF303030,
        TextPrimary: 0xFF808080,
        TraceWave: 0xFF404040,
        TraceTick: green,
        TraceTock: red,
        VarioBad: cursor);

    [Fact]
    public void Markers_ColorAGreenAndCRedByEventType()
    {
        var sweepPlot = new AvaPlot();
        var renderer = new ScopeSweepRenderer(sweepPlot, new TextBlock(), "Arial");
        renderer.CreateGraphs();

        const uint green = 0xFF2C9118;
        const uint red = 0xFFD22222;
        const uint cursor = 0xFF0000FF; // distinct so the dotted review cursor is excluded
        renderer.ApplyTheme(Palette(green, red, cursor));

        var lines = sweepPlot.Plot.GetPlottables<VerticalLine>().ToList();
        var aMarkers = lines.Where(l => l.LinePattern.Equals(LinePattern.Dashed)).ToList();
        var cMarkers = lines
            .Where(l => l.LinePattern.Equals(LinePattern.Dotted) && !l.LineColor.Equals(Color.FromARGB(cursor)))
            .ToList();

        Assert.NotEmpty(aMarkers);
        Assert.NotEmpty(cMarkers);
        Assert.All(aMarkers, l => Assert.Equal(Color.FromARGB(green), l.LineColor));
        Assert.All(cMarkers, l => Assert.Equal(Color.FromARGB(red), l.LineColor));
    }
}
