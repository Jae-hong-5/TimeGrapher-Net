using System.Reflection;
using ScottPlot.Avalonia;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MultiFilterScopeRendererTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void CreateGraphsUsesCompactAxisPanelsOnAllLanes()
    {
        AvaPlot[] plots = CreatePlots();
        var renderer = new MultiFilterScopeRenderer(plots);

        renderer.CreateGraphs();

        for (int i = 0; i < plots.Length; i++)
        {
            float expectedLeft = i % 2 == 0 ? PlotThemeHelper.CompactLeftAxisSizePx : 44f;
            float expectedBottom = i >= plots.Length - 2 ? PlotThemeHelper.CompactBottomAxisSizePx : 36f;
            Assert.Equal(expectedLeft, plots[i].Plot.Axes.Left.MinimumSize);
            Assert.Equal(expectedBottom, plots[i].Plot.Axes.Bottom.MinimumSize);
        }
    }

    [Fact]
    public void RenderFrame_DoesNotDrawFilterSeriesBeforeBeatSync()
    {
        var renderer = new MultiFilterScopeRenderer(CreatePlots());
        renderer.CreateGraphs();
        AnalysisFrame frame = FilterFrame(beatSynced: false);

        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate));

        Assert.All(LaneBuffers(renderer), Assert.Empty);
    }

    [Fact]
    public void RenderFrame_DrawsTheSameFilterSeriesAfterBeatSync()
    {
        var renderer = new MultiFilterScopeRenderer(CreatePlots());
        renderer.CreateGraphs();
        AnalysisFrame frame = FilterFrame(beatSynced: false);
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate));

        frame.BeatSynced = true;
        frame.BeatSegments = BeatSegments();
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate));

        Assert.All(LaneBuffers(renderer), lane => Assert.NotEmpty(lane));
    }

    [Fact]
    public void RenderFrame_ClearsFilterSeriesAfterSyncLoss()
    {
        var renderer = new MultiFilterScopeRenderer(CreatePlots());
        renderer.CreateGraphs();
        AnalysisFrame frame = FilterFrame(beatSynced: true);
        frame.BeatSegments = BeatSegments();
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate));

        frame.BeatSynced = false;
        frame.BeatSegments = null;
        renderer.RenderFrame(frame, new AnalysisTabRenderContext(SampleRate));

        Assert.All(LaneBuffers(renderer), Assert.Empty);
    }

    private static AvaPlot[] CreatePlots() =>
        Enumerable.Range(0, MultiFilterScopeLanes.All.Count)
            .Select(_ => new AvaPlot())
            .ToArray();

    private static AnalysisFrame FilterFrame(bool beatSynced)
    {
        var frame = new AnalysisFrame
        {
            BeatSynced = beatSynced,
        };
        double[] x = Enumerable.Range(0, 25)
            .Select(i => i * 1000.0)
            .ToArray();
        for (int lane = 0; lane < MultiFilterScopeLanes.All.Count; lane++)
        {
            double scale = lane + 1;
            AddScopeSeries(frame, new GraphSeriesFrame
            {
                Id = MultiFilterScopeLanes.All[lane].SeriesId,
                X = x,
                Y = x.Select(value => scale * (1.0 + value / 10000.0)).ToArray(),
                Replace = true,
            });
        }

        return frame;
    }

    private static BeatSegmentsSnapshot BeatSegments() => new()
    {
        Version = 1,
        Segments = new[]
        {
            Segment(0.10),
            Segment(0.20),
            Segment(0.30),
        },
    };

    private static BeatSegment Segment(double startTimeS) => new()
    {
        StartTimeS = startTimeS,
        AOffsetMs = 0.0,
        Samples = new float[] { 1.0f },
        MsPerPoint = 0.25,
    };

    private static IReadOnlyList<List<double>> LaneBuffers(MultiFilterScopeRenderer renderer) =>
        (IReadOnlyList<List<double>>)typeof(MultiFilterScopeRenderer)
            .GetField("_x", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;

    private static void AddScopeSeries(AnalysisFrame frame, GraphSeriesFrame series)
    {
        typeof(AnalysisFrame)
            .GetMethod("AddScopeSeries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(frame, new object[] { series });
    }
}
