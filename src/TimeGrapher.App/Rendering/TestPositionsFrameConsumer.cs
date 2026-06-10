using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class TestPositionsFrameConsumer : IAnalysisFrameConsumer
{
    private readonly TestPositionsRenderer _renderer;

    public TestPositionsFrameConsumer(TestPositionsRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.TestPositionsTabId;

    public void Initialize(AnalysisTabResetContext context)
    {
        _renderer.Reset();
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _renderer.Reset();
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        // History is cumulative on the frame; nothing to accumulate UI-side.
        _ = frame;
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // No time axis on this tab, so the review-cursor contract does not apply.
        _ = context;
        _renderer.RenderFrame(frame);
    }
}
