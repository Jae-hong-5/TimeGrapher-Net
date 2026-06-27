using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
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

    [Fact]
    public void FirstSweepFrameFitsDataWithoutResetView()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();
        var frame = new AnalysisFrame();
        AddScopeSeries(frame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.2, 3.0, 6.0, 3.0, 0.2 },
        });

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));
        AxisLimits limits = sweepPlot.Plot.Axes.GetLimits();

        Assert.InRange(limits.Left, -10.1, -9.9);
        Assert.InRange(limits.Right, 249.9, 250.1);
        Assert.True(limits.Top > 5.5, $"expected first render to fit data Y, got top {limits.Top}");
        Assert.True(limits.Bottom < 0.3, $"expected first render to fit data Y, got bottom {limits.Bottom}");
    }

    [Fact]
    public void StartupZeroSweepDoesNotLockInvisibleYRange()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();
        var zeroFrame = new AnalysisFrame();
        AddScopeSeries(zeroFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
        });
        var signalFrame = new AnalysisFrame();
        AddScopeSeries(signalFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.2, 3.0, 6.0, 3.0, 0.2 },
        });

        renderer.RenderFrame(zeroFrame, new AnalysisTabRenderContext(48000));
        renderer.RenderFrame(signalFrame, new AnalysisTabRenderContext(48000));
        AxisLimits limits = sweepPlot.Plot.Axes.GetLimits();

        Assert.InRange(limits.Left, -10.1, -9.9);
        Assert.InRange(limits.Right, 249.9, 250.1);
        Assert.True(limits.Top > 5.5, $"expected post-startup render to fit data Y, got top {limits.Top}");
        Assert.True(limits.Bottom < 0.3, $"expected post-startup render to fit data Y, got bottom {limits.Bottom}");
    }

    private static void AddScopeSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddScopeSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }
}
