using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Pins the pooled scope-marker recolor on theme toggle: after a stop no frame
/// re-render refreshes the marker pool, so ApplyTheme alone must restyle the
/// pooled lines and labels from their recorded source colors.
/// </summary>
public sealed class RateScopeRendererThemeTests
{
    private static PlotThemePalette Palette(uint tick, uint text) => new(
        SurfaceBg: 0xFF101010,
        ScopeBg: 0xFF202020,
        ScopeGrid: 0xFF303030,
        TextPrimary: text,
        TraceWave: 0xFF404040,
        TraceTick: tick,
        TraceTock: 0xFF505050);

    [Fact]
    public void ThemeToggleRecolorsPooledScopeMarkers()
    {
        var scopePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, new AvaPlot(), "Arial");
        renderer.CreateGraphs(rateErrorYScale: 1.0, rateDataPoints: 600);

        renderer.UpdateScopeMarkers(
            new[] { new ScopeVerticalMarker { X = 1.0, Height = 0.5, Color = Argb.Green } },
            Array.Empty<ScopeHorizontalMarker>(),
            new[]
            {
                new ScopeTextMarker
                {
                    X = 1.0,
                    Height = 0.5,
                    Text = "A",
                    Color = Argb.Black,
                    Alignment = MarkerTextAlignment.LeftTop,
                },
            });

        PlotThemePalette dark = Palette(tick: 0xFF112233, text: 0xFFAABBCC);
        renderer.ApplyTheme(dark);

        LinePlot line = scopePlot.Plot.GetPlottables<LinePlot>().Single();
        Text label = scopePlot.Plot.GetPlottables<Text>().Single();
        Assert.Equal(Color.FromARGB(0xFF112233), line.LineColor);
        Assert.Equal(Color.FromARGB(0xFFAABBCC), label.LabelFontColor);

        PlotThemePalette light = Palette(tick: 0xFF445566, text: 0xFF001122);
        renderer.ApplyTheme(light);

        Assert.Equal(Color.FromARGB(0xFF445566), line.LineColor);
        Assert.Equal(Color.FromARGB(0xFF001122), label.LabelFontColor);
    }
}
