using Avalonia.Controls;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class GraphLabelFontSizeTests
{
    private const float ExpectedGraphLabelFontSize = PlotThemeHelper.GraphLabelFontSize;

    [Fact]
    public void RenderersUseSharedGraphLabelFontSize()
    {
        var plots = new List<AvaPlot>();

        AddBeatNoisePlots(plots);
        AddEscapementAnalyzerPlot(plots);
        AddLongTermPerfPlots(plots);
        AddRateScopePlot(plots);
        AddScopeSweepPlot(plots);
        AddTraceDisplayPlots(plots);
        AddVarioPlots(plots);
        AddWaveformComparePlot(plots);

        Text[] textLabels = plots
            .SelectMany(plot => plot.Plot.GetPlottables<Text>())
            .ToArray();
        // TraceDisplay's average readout is an Annotation, not a Text, so the
        // standardized size must be asserted over that type too (it was changed
        // 12 -> 14 in the same pass and would otherwise be uncovered).
        Annotation[] annotationLabels = plots
            .SelectMany(plot => plot.Plot.GetPlottables<Annotation>())
            .ToArray();

        Assert.NotEmpty(textLabels);
        Assert.NotEmpty(annotationLabels);
        Assert.All(textLabels, label => Assert.Equal(ExpectedGraphLabelFontSize, label.LabelFontSize));
        Assert.All(annotationLabels, label => Assert.Equal(ExpectedGraphLabelFontSize, label.LabelFontSize));
    }

    private static void AddBeatNoisePlots(List<AvaPlot> plots)
    {
        var mainPlot = new AvaPlot();
        var stripPlot = new AvaPlot();
        var averagePlot = new AvaPlot();
        var renderer = new BeatNoiseScopeRenderer(
            mainPlot,
            stripPlot,
            averagePlot,
            new TextBlock());

        renderer.CreateGraphs();

        plots.Add(mainPlot);
        plots.Add(stripPlot);
        plots.Add(averagePlot);
    }

    private static void AddEscapementAnalyzerPlot(List<AvaPlot> plots)
    {
        var plot = new AvaPlot();
        var renderer = new EscapementAnalyzerRenderer(
            plot,
            EscapementReadout.Labels.Select(_ => new TextBlock()).ToArray(),
            "Arial");

        renderer.CreateGraphs();

        plots.Add(plot);
    }

    private static void AddLongTermPerfPlots(List<AvaPlot> plots)
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var beatErrorPlot = new AvaPlot();
        var renderer = new LongTermPerfRenderer(ratePlot, amplitudePlot, beatErrorPlot);

        renderer.CreateGraphs();

        plots.Add(ratePlot);
        plots.Add(amplitudePlot);
        plots.Add(beatErrorPlot);
    }

    private static void AddRateScopePlot(List<AvaPlot> plots)
    {
        var scopePlot = new AvaPlot();
        var renderer = new RateScopeRenderer(scopePlot, new AvaPlot(), "Arial");

        renderer.CreateGraphs(rateErrorYScale: 1.0, rateDataPoints: 600);
        renderer.UpdateScopeMarkers(
            Array.Empty<ScopeVerticalMarker>(),
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

        plots.Add(scopePlot);
    }

    private static void AddScopeSweepPlot(List<AvaPlot> plots)
    {
        var plot = new AvaPlot();
        var readoutValues = ScopeSweepReadout.Labels.Select(_ => new TextBlock()).ToArray();
        var renderer = new ScopeSweepRenderer(plot, readoutValues);

        renderer.CreateGraphs();

        plots.Add(plot);
    }

    private static void AddTraceDisplayPlots(List<AvaPlot> plots)
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var renderer = new TraceDisplayRenderer(
            ratePlot,
            amplitudePlot,
            new Border(),
            new TextBlock());

        renderer.CreateGraphs();

        plots.Add(ratePlot);
        plots.Add(amplitudePlot);
    }

    private static void AddVarioPlots(List<AvaPlot> plots)
    {
        var ratePlot = new AvaPlot();
        var amplitudePlot = new AvaPlot();
        var summary = new VarioSummaryControls(
            new TextBlock(),
            new TextBlock(),
            new TextBlock());
        var readouts = new VarioReadoutControls(BuildCells(), BuildCells());
        var renderer = new VarioRenderer(
            ratePlot,
            amplitudePlot,
            summary,
            readouts,
            "Arial");

        renderer.CreateGraphs();

        plots.Add(ratePlot);
        plots.Add(amplitudePlot);
    }

    private static void AddWaveformComparePlot(List<AvaPlot> plots)
    {
        var plot = new AvaPlot();
        var renderer = new WaveformCompareRenderer(plot, new TextBlock(), "Arial");

        renderer.CreateGraphs();

        plots.Add(plot);
    }

    private static TextBlock[] BuildCells() =>
        Enumerable.Range(0, VarioRenderer.CellCount)
            .Select(_ => new TextBlock())
            .ToArray();
}
