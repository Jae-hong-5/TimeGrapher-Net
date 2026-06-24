using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the Sweep tab marker coloring: both A (dashed) and C (dotted) escapement
/// markers use TextPrimary, matching the Waveform Compare A/C guide color so
/// both tabs share the same escapement-event color contract. A markers are dashed
/// and C markers dotted; the dotted review cursor is excluded by its distinct color.
/// </summary>
public sealed class ScopeSweepRendererThemeTests
{
    private static PlotThemePalette Palette(uint textPrimary, uint green, uint red, uint cursor) => new(
        SurfaceBg: 0xFF101010,
        ScopeBg: 0xFF202020,
        ScopeGrid: 0xFF303030,
        TextPrimary: textPrimary,
        TraceWave: 0xFF404040,
        TraceTick: green,
        TraceTock: red,
        VarioBad: cursor);

    [Fact]
    public void Markers_ColorBothAAndCWithTextPrimary()
    {
        var sweepPlot = new AvaPlot();
        var renderer = new ScopeSweepRenderer(sweepPlot, new TextBlock(), "Arial");
        renderer.CreateGraphs();

        const uint textPrimary = 0xFF1A1A1A;
        const uint green = 0xFF2C9118;
        const uint red = 0xFFD22222;
        const uint cursor = 0xFF0000FF; // distinct so the dotted review cursor is excluded
        renderer.ApplyTheme(Palette(textPrimary, green, red, cursor));

        var lines = sweepPlot.Plot.GetPlottables<VerticalLine>().ToList();
        var aMarkers = lines.Where(l => l.LinePattern.Equals(LinePattern.Dashed)).ToList();
        var cMarkers = lines
            .Where(l => l.LinePattern.Equals(LinePattern.Dotted) && !l.LineColor.Equals(Color.FromARGB(cursor)))
            .ToList();

        Assert.NotEmpty(aMarkers);
        Assert.NotEmpty(cMarkers);
        Assert.All(aMarkers, l => Assert.Equal(Color.FromARGB(textPrimary), l.LineColor));
        Assert.All(cMarkers, l => Assert.Equal(Color.FromARGB(textPrimary), l.LineColor));
    }
}
