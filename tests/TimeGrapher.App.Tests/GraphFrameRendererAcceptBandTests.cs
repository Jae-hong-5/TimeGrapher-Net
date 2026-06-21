using Avalonia.Controls;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// GraphFrameRenderer.ApplyAcceptBands must fan out to exactly the consumers that
/// opt in via IAcceptBandConsumer, and never touch plain consumers — the same
/// contract as the themed fan-out, so a banded tab participates by implementing
/// the interface rather than being special-cased.
/// </summary>
public sealed class GraphFrameRendererAcceptBandTests
{
    private sealed class BandConsumer : IAnalysisFrameConsumer, IAcceptBandConsumer
    {
        public int ApplyCalls { get; private set; }
        public string TabId => "band";
        public void Initialize(AnalysisTabResetContext context) { }
        public void Reset(AnalysisTabResetContext context) { }
        public void ObserveFrame(AnalysisFrame frame) { }
        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context) { }
        public void ApplyAcceptBands() => ApplyCalls++;
    }

    private sealed class PlainConsumer : IAnalysisFrameConsumer
    {
        public string TabId => "plain";
        public void Initialize(AnalysisTabResetContext context) { }
        public void Reset(AnalysisTabResetContext context) { }
        public void ObserveFrame(AnalysisFrame frame) { }
        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context) { }
    }

    [Fact]
    public void ApplyAcceptBands_InvokesOnlyBandConsumers()
    {
        var band1 = new BandConsumer();
        var band2 = new BandConsumer();
        var plain = new PlainConsumer();
        var renderer = new GraphFrameRenderer(
            new IAnalysisFrameConsumer[] { band1, plain, band2 }, new TextBlock());

        renderer.ApplyAcceptBands();

        Assert.Equal(1, band1.ApplyCalls);
        Assert.Equal(1, band2.ApplyCalls);
        // plain has no ApplyAcceptBands to call; the cast-filter must simply skip it
        // (reaching here without throwing confirms the OfType fan-out excluded it).
    }
}
