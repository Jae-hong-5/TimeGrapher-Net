using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class ScopeSweepRendererThemeTests
{
    private static PlotThemePalette Palette(uint traceWave, uint green, uint red, uint cursor) => new(
        SurfaceBg: 0xFF101010,
        ScopeBg: 0xFF202020,
        ScopeGrid: 0xFF303030,
        TextPrimary: 0xFF1A1A1A,
        TraceWave: traceWave,
        TraceTick: green,
        TraceTock: red,
        VarioBad: cursor);

    [Fact]
    public void ApplyTheme_UsesEscapementColorContract()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();

        Assert.Equal(PlotThemeHelper.CompactLeftAxisSizePx, sweepPlot.Plot.Axes.Left.MinimumSize);
        Assert.Equal(PlotThemeHelper.CompactBottomAxisSizePx, sweepPlot.Plot.Axes.Bottom.MinimumSize);

        const uint traceWave = 0xFF404040;
        const uint green = 0xFF2C9118;
        const uint red = 0xFFD22222;
        const uint cursor = 0xFF0000FF; // distinct so the dotted review cursor is excluded
        renderer.ApplyTheme(Palette(traceWave, green, red, cursor));

        var scatters = sweepPlot.Plot.GetPlottables<Scatter>().ToList();
        var dataScatters = scatters
            .Where(scatter => string.IsNullOrEmpty(scatter.LegendText))
            .ToList();
        var legendEntries = scatters
            .Where(scatter => !string.IsNullOrEmpty(scatter.LegendText))
            .ToList();
        var lines = sweepPlot.Plot.GetPlottables<VerticalLine>().ToList();
        var aMarkers = lines
            .Where(l => l.LineColor.Equals(Color.FromARGB(green)))
            .ToList();
        var cMarkers = lines
            .Where(l => l.LineColor.Equals(Color.FromARGB(red)))
            .ToList();

        Assert.Equal(2, dataScatters.Count);
        Assert.All(dataScatters, s => Assert.Equal(Color.FromARGB(traceWave), s.LineColor));
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "A" && scatter.LineColor.Equals(Color.FromARGB(green)));
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "C" && scatter.LineColor.Equals(Color.FromARGB(red)));
        Assert.NotEmpty(aMarkers);
        Assert.NotEmpty(cMarkers);
        Assert.All(aMarkers, l => Assert.Equal(GraphLinePatterns.VerticalGuide, l.LinePattern));
        Assert.All(cMarkers, l => Assert.Equal(GraphLinePatterns.VerticalGuide, l.LinePattern));
        Assert.All(aMarkers, l => Assert.Equal(Color.FromARGB(green), l.LineColor));
        Assert.All(cMarkers, l => Assert.Equal(Color.FromARGB(red), l.LineColor));
    }

    [Fact]
    public void CreateGraphs_DoesNotPlaceMovingAAndCTextLabels()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);

        renderer.CreateGraphs();

        Scatter[] legendEntries = sweepPlot.Plot.GetPlottables<Scatter>()
            .Where(scatter => !string.IsNullOrEmpty(scatter.LegendText))
            .ToArray();
        Assert.Empty(sweepPlot.Plot.GetPlottables<Text>());
        Assert.True(sweepPlot.Plot.Legend.IsVisible);
        Assert.Equal(Alignment.LowerRight, sweepPlot.Plot.Legend.Alignment);
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "A" && Equals(scatter.LinePattern, GraphLinePatterns.VerticalGuide) && scatter.LineWidth >= 2);
        Assert.Contains(legendEntries, scatter => scatter.LegendText == "C" && Equals(scatter.LinePattern, GraphLinePatterns.VerticalGuide) && scatter.LineWidth >= 2);
    }
}
