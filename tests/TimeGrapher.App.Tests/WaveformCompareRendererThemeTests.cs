using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class WaveformCompareRendererThemeTests
{
    private static PlotThemePalette Palette(uint traceWave, uint green, uint red) => new(
        SurfaceBg: 0xFF101010,
        ScopeBg: 0xFF202020,
        ScopeGrid: 0xFF303030,
        TextPrimary: 0xFF1A1A1A,
        TraceWave: traceWave,
        TraceTick: green,
        TraceTock: red);

    [Fact]
    public void ApplyTheme_UsesEscapementColorContract()
    {
        var plot = new AvaPlot();
        var renderer = new WaveformCompareRenderer(plot, new TextBlock(), "Arial");
        renderer.CreateGraphs();

        const uint traceWave = 0xFF404040;
        const uint green = 0xFF2C9118;
        const uint red = 0xFFD22222;
        renderer.ApplyTheme(Palette(traceWave, green, red));

        var visibleLaneScatters = plot.Plot.GetPlottables<Scatter>()
            .Where(scatter => scatter.IsVisible)
            .ToList();
        var aGuides = plot.Plot.GetPlottables<VerticalLine>()
            .Where(line => line.LinePattern.Equals(LinePattern.Dashed))
            .ToList();
        var cGuides = plot.Plot.GetPlottables<LinePlot>().ToList();

        Assert.Equal(WaveformCompareLogic.PairLanes * 2, visibleLaneScatters.Count);
        Assert.All(visibleLaneScatters, scatter => Assert.Equal(Color.FromARGB(traceWave), scatter.LineColor));
        Assert.Equal(2, aGuides.Count);
        Assert.All(aGuides, guide => Assert.Equal(Color.FromARGB(green), guide.LineColor));
        Assert.Equal(WaveformCompareLogic.PairLanes * 2, cGuides.Count);
        Assert.All(cGuides, guide => Assert.Equal(Color.FromARGB(red), guide.LineColor));
    }
}
