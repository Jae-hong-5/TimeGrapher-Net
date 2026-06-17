using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class EscapementAnalyzerRendererTests
{
    [Fact]
    public void RenderFrameScalesScopeFromRawMinMaxInsteadOfEnvelope()
    {
        var plot = new AvaPlot();
        var valueTexts = new TextBlock[EscapementReadout.Labels.Length];
        for (int i = 0; i < valueTexts.Length; i++)
        {
            valueTexts[i] = new TextBlock();
        }

        var renderer = new EscapementAnalyzerRenderer(plot, valueTexts, "Arial");
        renderer.CreateGraphs();

        var segment = new BeatSegment
        {
            Samples = new float[] { 9.0f, 0.1f, 0.1f, 0.1f },
            RawValid = true,
            RawMin = new float[] { 0.0f, -0.6f, 0.0f, 0.0f },
            RawMax = new float[] { 0.8f, 0.1f, 0.0f, 0.0f },
            MsPerPoint = 0.25,
            AOffsetMs = 0.0,
        };
        var frame = new AnalysisFrame
        {
            BeatSegments = new BeatSegmentsSnapshot
            {
                Version = 1,
                Segments = new[] { segment },
            },
        };

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate: 48000));

        AxisLimits limits = plot.Plot.Axes.GetLimits();
        Assert.InRange(limits.Top, 0.87, 0.89);
        Assert.InRange(limits.Bottom, -0.89, -0.87);
    }
}
