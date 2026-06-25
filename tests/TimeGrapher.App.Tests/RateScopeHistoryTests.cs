using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// The Rate/Scope renderer accumulates the producer's latest-window slices into a
/// rolling history (so a pan/pause can reach back without the producer re-copying the
/// whole retention) and reduces only the visible X range for display.
/// </summary>
public sealed class RateScopeHistoryTests
{
    [Theory]
    [InlineData(0.0, 0.0, 150.0)]
    [InlineData(149.999, 0.0, 150.0)]
    [InlineData(150.0, 150.0, 300.0)]
    [InlineData(299.999, 150.0, 300.0)]
    [InlineData(300.0, 300.0, 450.0)]
    public void RatePageWindowFor_AdvancesInFixedPages(double maxBeat, double expectedLeft, double expectedRight)
    {
        (double left, double right) = RateScopeRenderer.RatePageWindowFor(maxBeat);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedRight, right);
    }

    [Fact]
    public void RenderFrame_ShowsAveragePeriodRateIntervalAnnotations()
    {
        var scopePlot = new AvaPlot();
        var ratePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, ratePlot, "Arial");
        renderer.CreateGraphs(rateErrorYScale: 10.0, rateDataPoints: 600);
        AssertAllowsMousePanOnly(ratePlot);
        renderer.ApplyTheme(new PlotThemePalette(
            SurfaceBg: 0xFF101010,
            ScopeBg: 0xFF202020,
            ScopeGrid: 0xFF303030,
            TextPrimary: 0xFFFAFAFA,
            TraceWave: 0xFF404040,
            TraceTick: 0xFF112233,
            TraceTock: 0xFF445566,
            AveragePeriodAnnotation: 0xFF9A9A9A,
            AveragePeriodAnnotationAlternate: 0xFFC4C4C4));

        var frame = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                AveragePeriodRateIntervals = new[]
                {
                    new AveragePeriodRateInterval(
                        0.0, 4.0, 0.0, 3.0, 1728.0,
                        AmplitudeValid: true, AmplitudeDeg: 280.0,
                        BeatErrorValid: true, BeatErrorMs: 0.2),
                    new AveragePeriodRateInterval(
                        4.0, 8.0, 3.0, 6.0, -864.0,
                        AmplitudeValid: false, AmplitudeDeg: 0.0,
                        BeatErrorValid: false, BeatErrorMs: 0.0),
                },
            },
        };
        AddRateSeries(frame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 0.0, 4.0, 8.0 },
            Y = new[] { 0.0, 1.0, -1.0 },
            Replace = true,
        });

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));

        Assert.DoesNotContain(ratePlot.Plot.GetPlottables<HorizontalSpan>(), s => s.IsVisible);
        VerticalLine[] boundaries = ratePlot.Plot.GetPlottables<VerticalLine>().Where(line => line.IsVisible).ToArray();
        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, boundaries.Select(line => line.X).ToArray());
        Assert.All(boundaries, line =>
        {
            Assert.Equal(LinePattern.Dashed, line.LinePattern);
            Assert.False(line.EnableAutoscale);
        });
        Text[] labels = ratePlot.Plot.GetPlottables<Text>().Where(t => t.IsVisible).ToArray();
        Assert.Equal(
            new[]
            {
                "+1728.0 s/d\n280°  0.2 ms",
                "-864.0 s/d\n---°  ---- ms",
            },
            labels.Select(t => t.LabelText).ToArray());
    }

    [Fact]
    public void ResetScopeView_RestoresDefaultLiveWindow()
    {
        var scopePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, new AvaPlot(), "Arial");
        renderer.CreateGraphs(rateErrorYScale: 10.0, rateDataPoints: 600);

        var frame = new AnalysisFrame { GraphTickEnd = 48000 };
        AddScopeSeries(frame, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.ScopePcm,
            X = new[] { 0.0, 12000.0, 24000.0, 36000.0, 48000.0 },
            Y = new[] { 0.0, 0.05, 0.1, 0.05, 0.0 },
            Replace = true,
        });
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(48000));

        SetPrivateField(renderer, "_scopeFollowLive", false);
        scopePlot.Plot.Axes.SetLimitsX(0.0, 48000.0);
        scopePlot.Plot.Axes.SetLimitsY(100.0, 110.0);

        renderer.ResetScopeView();
        AxisLimits limits = scopePlot.Plot.Axes.GetLimits();

        Assert.Equal(24000.0, limits.Left);
        Assert.Equal(48000.0, limits.Right);
        Assert.True(limits.Bottom < 0.05);
        Assert.True(limits.Top > 0.05);
    }

    [Fact]
    public void RenderFrame_UpdatesRatePointsAfterUserDropsRateFollow()
    {
        var scopePlot = new AvaPlot();
        var ratePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, ratePlot, "Arial");
        renderer.CreateGraphs(rateErrorYScale: 10.0, rateDataPoints: 600);

        var first = new AnalysisFrame();
        AddRateSeries(first, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 0.0, 1.0 },
            Y = new[] { 0.0, 0.1 },
            Replace = true,
        });
        renderer.RenderFrame(first, new AnalysisTabRenderContext(48000));

        SetPrivateField(renderer, "_rateFollowLive", false);
        ratePlot.Plot.Axes.SetLimitsX(0.0, RateScopeRenderer.RatePageWindowBeats);

        var second = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                AveragePeriodRateIntervals = new[]
                {
                    new AveragePeriodRateInterval(
                        150.0, 151.0, 150.0, 151.0, -432.0,
                        AmplitudeValid: true, AmplitudeDeg: 250.0,
                        BeatErrorValid: true, BeatErrorMs: 0.4),
                },
            },
        };
        AddRateSeries(second, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 150.0, 151.0 },
            Y = new[] { 1.2, 1.21 },
            Replace = true,
        });
        renderer.RenderFrame(second, new AnalysisTabRenderContext(48000));

        Assert.Equal(new[] { 150.0, 151.0 }, RateX(renderer, AnalysisGraphSeries.RateTic));
        Assert.Equal(
            new[] { "-432.0 s/d\n250°  0.4 ms" },
            ratePlot.Plot.GetPlottables<Text>().Where(t => t.IsVisible).Select(t => t.LabelText).ToArray());

        ratePlot.Plot.Axes.SetLimitsX(0.0, RateScopeRenderer.RatePageWindowBeats / 2.0);
        ratePlot.Plot.Axes.SetLimitsY(100.0, 110.0);
        ratePlot.Plot.RenderInMemory(900, 240);
        AxisLimits limits = ratePlot.Plot.Axes.GetLimits();
        Assert.Equal(0.0, limits.Left);
        Assert.Equal(RateScopeRenderer.RatePageWindowBeats, limits.Right);
        Assert.Equal(-10.0, limits.Bottom);
        Assert.Equal(10.0, limits.Top);
    }

    private static void AddRateSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddRateSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }

    private static void AddScopeSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddScopeSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        target.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);
    }

    private static double[] RateX(RateScopeRenderer renderer, string seriesId)
    {
        var series = (GraphSeriesDefinition[])typeof(RateScopeRenderer)
            .GetField("_rateSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(renderer)!;
        var xs = (List<double>[])typeof(RateScopeRenderer)
            .GetField("_rateX", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(renderer)!;

        int index = Array.FindIndex(series, spec => spec.Id == seriesId);
        Assert.True(index >= 0, "Expected rate series to be registered.");
        return xs[index].ToArray();
    }

    private static void AssertAllowsMousePanOnly(AvaPlot plot)
    {
        MouseDragPan pan = plot.UserInputProcessor.UserActionResponses
            .OfType<MouseDragPan>()
            .Single();

        Assert.True(pan.LockY);
        Assert.False(pan.LockX);
        Assert.DoesNotContain(plot.UserInputProcessor.UserActionResponses, response =>
            response is MouseWheelZoom or MouseDragZoom or MouseDragZoomRectangle);
    }

    [Fact]
    public void MergeScopeSlice_AppendsAndIsIdempotentOnReplay()
    {
        var hx = new List<double>();
        var hy = new List<double>();

        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0 }, new[] { 0.0, 10.0, 20.0 }, retentionSamples: 1000);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, hy);

        // The throttled producer re-attaches the same slice between rebuilds: merging
        // it again must not duplicate or shift anything.
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0 }, new[] { 0.0, 10.0, 20.0 }, retentionSamples: 1000);
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, hy);
    }

    [Fact]
    public void MergeScopeSlice_OverlapSupersedesTailThenExtends()
    {
        var hx = new List<double>();
        var hy = new List<double>();
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 0.0, 1.0, 2.0, 3.0, 4.0 }, new[] { 0.0, 10.0, 20.0, 30.0, 40.0 }, 1000);

        // A fresh slice starting at X=3 supersedes the overlapping tail (3,4) with its
        // newer Y values and extends the history to 6.
        RateScopeRenderer.MergeScopeSlice(hx, hy, new[] { 3.0, 4.0, 5.0, 6.0 }, new[] { 300.0, 400.0, 500.0, 600.0 }, 1000);

        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, hx);
        Assert.Equal(new[] { 0.0, 10.0, 20.0, 300.0, 400.0, 500.0, 600.0 }, hy);
    }

    [Fact]
    public void MergeScopeSlice_TrimsFrontToRetention()
    {
        var hx = new List<double>();
        var hy = new List<double>();

        // newest = 10, retention 5 -> keep only X >= 10 - 5 = 5.
        RateScopeRenderer.MergeScopeSlice(
            hx, hy,
            new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 },
            new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 },
            retentionSamples: 5);

        Assert.Equal(new[] { 6.0, 8.0, 10.0 }, hx);
        Assert.Equal(new[] { 6.0, 8.0, 10.0 }, hy);
    }

    [Fact]
    public void ReduceRangeTo_KeepsVisibleRangePlusOneNeighbourEachSide()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 3.0, right: 6.0, targetPointBudget: 100, tx, ty);

        // Visible [3,6] plus the neighbour just left (2) so the line reaches the edge.
        Assert.Equal(new[] { 2.0, 3.0, 4.0, 5.0, 6.0 }, tx);
        Assert.Equal(new[] { 20.0, 30.0, 40.0, 50.0, 60.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_IncludesNeighbourJustRightOfViewEdge()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        // Non-exact right edge (6.5): the reduction must include 7.0, the point just
        // right of the view, so the drawn line reaches the edge. (right: 6.0 lands on a
        // sample, so it never exercises the right-neighbour inclusion.)
        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 3.0, right: 6.5, targetPointBudget: 100, tx, ty);

        Assert.Equal(new[] { 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 }, tx);
        Assert.Equal(new[] { 20.0, 30.0, 40.0, 50.0, 60.0, 70.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_SubsamplesToBudget()
    {
        var sx = Enumerable.Range(0, 10).Select(i => (double)i).ToList();
        var sy = sx.Select(v => v * 10.0).ToList();
        var tx = new List<double>();
        var ty = new List<double>();

        RateScopeRenderer.ReduceRangeTo(sx, sy, left: 0.0, right: 9.0, targetPointBudget: 3, tx, ty);

        // 10 points over budget 3 -> stride 4 (matches SeriesDataReducer).
        Assert.Equal(new[] { 0.0, 4.0, 8.0 }, tx);
        Assert.Equal(new[] { 0.0, 40.0, 80.0 }, ty);
    }

    [Fact]
    public void ReduceRangeTo_EmptySourceProducesEmptyTarget()
    {
        var tx = new List<double> { 1.0 };
        var ty = new List<double> { 1.0 };

        RateScopeRenderer.ReduceRangeTo(new List<double>(), new List<double>(), left: 0.0, right: 1.0, targetPointBudget: 10, tx, ty);

        Assert.Empty(tx);
        Assert.Empty(ty);
    }
}
