using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
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

    [Fact]
    public void RenderFrameFramesEarlyPairsAtTheFixedTopAnchoredAxis()
    {
        // Lanes are top-anchored: lane 0 (newest) always sits at the top, so the Y axis
        // top must reach the fixed top lane as soon as any pair exists, independent of how
        // many pairs are present. Before the fix the axis grew from the bottom with the
        // pair count, leaving early (<4) pairs drawn above the visible axis (blank tab).
        var plot = new AvaPlot();
        var renderer = new WaveformCompareRenderer(plot, new TextBlock(), "Arial");
        renderer.CreateGraphs();

        renderer.RenderFrame(FrameWithSegments(segmentCount: 2, version: 1), new AnalysisTabRenderContext(SampleRate: 48000));
        double onePairTop = plot.Plot.Axes.GetLimits().Top;

        renderer.RenderFrame(FrameWithSegments(segmentCount: 8, version: 2), new AnalysisTabRenderContext(SampleRate: 48000));
        double fourPairTop = plot.Plot.Axes.GetLimits().Top;

        Assert.Equal(fourPairTop, onePairTop, 6);
        // The top must clear lane 0's baseline ((PairLanes-1) * LaneSpacing) even for one pair.
        Assert.True(onePairTop >= (WaveformCompareLogic.PairLanes - 1) * WaveformCompareLogic.LaneSpacing);
    }

    private static AnalysisFrame FrameWithSegments(int segmentCount, ulong version)
    {
        var segments = new BeatSegment[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            segments[i] = new BeatSegment
            {
                Samples = new float[] { 0.5f, 0.5f },
                MsPerPoint = 0.25,
                AOffsetMs = 0.0,
                IsTic = (i % 2) == 0,
            };
        }

        return new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot { Version = version, Segments = segments },
        };
    }
}
