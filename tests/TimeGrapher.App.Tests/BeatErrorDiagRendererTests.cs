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
        Assert.Equal(PlotThemeHelper.CompactLeftAxisSizePx, tracePlot.Plot.Axes.Left.MinimumSize);
        Assert.Equal(PlotThemeHelper.CompactBottomAxisSizePx, tracePlot.Plot.Axes.Bottom.MinimumSize);
        AssertAllowsMousePanOnly(tracePlot);

        var first = new AnalysisFrame();
        AddRateSeries(first, new GraphSeriesFrame
        {
            Id = AnalysisGraphSeries.RateTic,
            X = new[] { 0.0, 1.0 },
            Y = new[] { 0.0, 0.1 },
            Replace = true,
        });
        renderer.RenderFrame(first, new AnalysisTabRenderContext(48000));

        AxisLimits firstLimits = tracePlot.Plot.Axes.GetLimits();
        Assert.Equal(0.0, firstLimits.Left);
        Assert.Equal(30.0, firstLimits.Right);
        Assert.Equal(BeatErrorDiagRenderer.TraceYMinMs, firstLimits.Bottom);
        Assert.Equal(12.0, firstLimits.Top);

        renderer.SetRateZoomFactor(4.0);
        AxisLimits zoomedLimits = tracePlot.Plot.Axes.GetLimits();
        Assert.Equal(0.0, zoomedLimits.Left);
        Assert.Equal(30.0, zoomedLimits.Right);
        Assert.Equal(-2.5, zoomedLimits.Bottom, 6);
        Assert.Equal(3.0, zoomedLimits.Top, 6);
        Assert.Equal("4x", renderer.RateZoomLabel);

        renderer.ResetView();
        AxisLimits resetLimits = tracePlot.Plot.Axes.GetLimits();
        Assert.Equal(0.0, resetLimits.Left);
        Assert.Equal(30.0, resetLimits.Right);
        Assert.Equal(BeatErrorDiagRenderer.TraceYMinMs, resetLimits.Bottom);
        Assert.Equal(12.0, resetLimits.Top);
        Assert.Equal("1x", renderer.RateZoomLabel);

        SetPrivateField(renderer, "_rateFollowLive", false);
        tracePlot.Plot.Axes.SetLimitsX(0.0, RateScopeRenderer.RatePageWindowBeats);

        var averageInterval = new AveragePeriodRateInterval(
            150.0, 151.0, 150.0, 151.0, -432.0,
            AmplitudeValid: true, AmplitudeDeg: 250.0,
            BeatErrorValid: true, BeatErrorMs: 0.4);
        var second = new AnalysisFrame
        {
            MetricsHistory = new BeatMetricsHistorySnapshot
            {
                AveragePeriodRateIntervals = new[]
                {
                    averageInterval,
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
            new[] { 150.0, 151.0 },
            tracePlot.Plot.GetPlottables<VerticalLine>().Where(line => line.IsVisible).Select(line => line.X).ToArray());
        Text label = Assert.Single(tracePlot.Plot.GetPlottables<Text>(), t => t.IsVisible);
        Assert.Equal(BeatErrorDiagRenderer.FormatAverageSegmentPlotLabel(averageInterval), label.LabelText);
        Assert.InRange(label.Location.Y, BeatErrorDiagRenderer.TraceYMaxMs, 12.0);

        AxisLimits autoLimits = tracePlot.Plot.Axes.GetLimits();
        Assert.Equal(BeatErrorDiagRenderer.TraceYMinMs, autoLimits.Bottom);
        Assert.Equal(12.0, autoLimits.Top);

        tracePlot.Plot.Axes.SetLimitsX(0.0, RateScopeRenderer.RatePageWindowBeats / 2.0);
        tracePlot.Plot.Axes.SetLimitsY(100.0, 110.0);
        tracePlot.Plot.RenderInMemory(900, 240);
        AxisLimits limits = tracePlot.Plot.Axes.GetLimits();
        Assert.Equal(0.0, limits.Left);
        Assert.Equal(RateScopeRenderer.RatePageWindowBeats / 2.0, limits.Right);
        Assert.Equal(BeatErrorDiagRenderer.TraceYMinMs, limits.Bottom);
        Assert.Equal(12.0, limits.Top);
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
}
