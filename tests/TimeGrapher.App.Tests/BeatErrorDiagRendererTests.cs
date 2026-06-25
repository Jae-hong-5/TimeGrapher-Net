using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class BeatErrorDiagRendererTests
{
    [Fact]
    public void RenderFrame_UpdatesRatePointsAfterUserDropsRateFollow()
    {
        var tracePlot = new AvaPlot();
        var renderer = new BeatErrorDiagRenderer(
            tracePlot,
            new Border(),
            new TextBlock(),
            BeatErrorReadout.Labels.Select(_ => new TextBlock()).ToArray(),
            "Arial");
        renderer.CreateGraphs(rateErrorYScale: 10.0, rateDataPoints: 600);
        AssertLocksMousePanAndDragZoomY(tracePlot);

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
        tracePlot.Plot.Axes.SetLimitsX(0.0, RateScopeRenderer.RatePageWindowBeats);

        var second = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                AveragePeriodRateIntervals = new[]
                {
                    new AveragePeriodRateInterval(
                        120.0, 121.0, 120.0, 121.0, -432.0,
                        AmplitudeValid: true, AmplitudeDeg: 250.0,
                        BeatErrorValid: true, BeatErrorMs: 0.4),
                },
            },
        };
        AddRateSeries(second, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 120.0, 121.0 },
            Y = new[] { 1.2, 1.21 },
            Replace = true,
        });
        renderer.RenderFrame(second, new AnalysisTabRenderContext(48000));

        Assert.Equal(new[] { 120.0, 121.0 }, RateX(renderer, AnalysisGraphSeries.RateTic));
        Assert.Equal(
            new[] { "-432.0 s/d\n250°  0.4 ms" },
            tracePlot.Plot.GetPlottables<Text>().Where(t => t.IsVisible).Select(t => t.LabelText).ToArray());

        tracePlot.Plot.Axes.SetLimitsY(100.0, 110.0);
        tracePlot.Plot.RenderInMemory(900, 240);
        AxisLimits limits = tracePlot.Plot.Axes.GetLimits();
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

    private static void SetPrivateField(object target, string name, object value)
    {
        target.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);
    }

    private static double[] RateX(BeatErrorDiagRenderer renderer, string seriesId)
    {
        var series = (GraphSeriesDefinition[])typeof(BeatErrorDiagRenderer)
            .GetField("_rateSeries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(renderer)!;
        var xs = (List<double>[])typeof(BeatErrorDiagRenderer)
            .GetField("_rateX", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(renderer)!;

        int index = Array.FindIndex(series, spec => spec.Id == seriesId);
        Assert.True(index >= 0, "Expected rate series to be registered.");
        return xs[index].ToArray();
    }

    private static void AssertLocksMousePanAndDragZoomY(AvaPlot plot)
    {
        MouseDragPan pan = plot.UserInputProcessor.UserActionResponses
            .OfType<MouseDragPan>()
            .Single();
        MouseDragZoom zoom = plot.UserInputProcessor.UserActionResponses
            .OfType<MouseDragZoom>()
            .Single();

        Assert.True(pan.LockY);
        Assert.False(pan.LockX);
        Assert.True(zoom.LockY);
        Assert.False(zoom.LockX);
    }
}
