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

    [Fact]
    public void LiveFollowGrowsLockedYToFitRampingSignal()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();

        // A faint first beat captures (and locks) the canonical Y at a small range.
        var faintFrame = new AnalysisFrame();
        AddScopeSeries(faintFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.05, 0.4, 0.8, 0.4, 0.05 },
        });
        renderer.RenderFrame(faintFrame, new AnalysisTabRenderContext(48000));
        AxisLimits afterFaint = sweepPlot.Plot.Axes.GetLimits();
        Assert.True(afterFaint.Top < 1.5, $"faint capture should fit the 0.8 peak, got top {afterFaint.Top}");

        // The signal then ramps to its steady-state amplitude. Live follow must
        // grow the locked Y so the trace is not clipped off the top (the "weird
        // graph until Reset View" symptom).
        var fullFrame = new AnalysisFrame();
        AddScopeSeries(fullFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.2, 3.0, 6.0, 3.0, 0.2 },
        });
        renderer.RenderFrame(fullFrame, new AnalysisTabRenderContext(48000));
        AxisLimits afterFull = sweepPlot.Plot.Axes.GetLimits();

        Assert.InRange(afterFull.Left, -10.1, -9.9);
        Assert.InRange(afterFull.Right, 249.9, 250.1);
        Assert.True(afterFull.Top > 5.5, $"expected live follow to grow Y to fit the 6.0 peak, got top {afterFull.Top}");
    }

    [Fact]
    public void LiveFollowDoesNotShrinkLockedYWhenSignalDrops()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();

        var fullFrame = new AnalysisFrame();
        AddScopeSeries(fullFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.2, 3.0, 6.0, 3.0, 0.2 },
        });
        renderer.RenderFrame(fullFrame, new AnalysisTabRenderContext(48000));

        // A sweep-multiple toggle briefly clears the bins (small/partial signal).
        // Y must not shrink back down, so toggling never rescales Y.
        var partialFrame = new AnalysisFrame();
        AddScopeSeries(partialFrame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.05, 0.4, 0.8, 0.4, 0.05 },
        });
        renderer.RenderFrame(partialFrame, new AnalysisTabRenderContext(48000));
        AxisLimits limits = sweepPlot.Plot.Axes.GetLimits();

        Assert.True(limits.Top > 5.5, $"expected Y to stay fitted to the 6.0 peak, got top {limits.Top}");
    }

    [Fact]
    public void LiveFollowDoesNotReZoomForSignalVariationWithinHeadroom()
    {
        var sweepPlot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(sweepPlot, readoutValues);
        renderer.CreateGraphs();

        // First fit captures the canonical Y tight to a 5.0 peak.
        RenderPeak(renderer, 5.0);

        // A louder beat (6.0) grows the locked Y with headroom above the peak.
        RenderPeak(renderer, 6.0);
        double topAfterGrow = sweepPlot.Plot.Axes.GetLimits().Top;
        Assert.True(topAfterGrow > 6.0, $"expected headroom above the 6.0 peak, got top {topAfterGrow}");

        // A slightly louder beat (6.5) that still fits under the headroom must
        // NOT re-zoom the view (the "graph keeps re-zooming" symptom).
        RenderPeak(renderer, 6.5);
        double topAfterVariation = sweepPlot.Plot.Axes.GetLimits().Top;
        Assert.Equal(topAfterGrow, topAfterVariation, 6);
    }

    private static void RenderPeak(ScopeSweepRenderer renderer, double peak)
    {
        var frame = new AnalysisFrame();
        AddScopeSeries(frame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.SweepTrace,
            Replace = true,
            X = new[] { 0.03125, 62.5, 125.0, 187.5, 249.96875 },
            Y = new[] { 0.2, peak / 2.0, peak, peak / 2.0, 0.2 },
        });
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));
    }

    private static void AddScopeSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddScopeSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }
}
