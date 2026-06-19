using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class WatchPositionsFrameConsumer : IAnalysisFrameConsumer
{
    private readonly WatchPositionsRenderer _positionRenderer;
    private readonly MultiPositionSeqRenderer _sequenceRenderer;

    public WatchPositionsFrameConsumer(
        WatchPositionsRenderer positionRenderer,
        MultiPositionSeqRenderer sequenceRenderer)
    {
        _positionRenderer = positionRenderer;
        _sequenceRenderer = sequenceRenderer;
    }

    public string TabId => InfoTabCatalog.WatchPositionsTabId;

    public void Initialize(AnalysisTabResetContext context)
    {
        _positionRenderer.Reset();
        _sequenceRenderer.Reset();
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _positionRenderer.Reset();
        _sequenceRenderer.Reset();
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        _positionRenderer.RenderFrame(frame);
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        // No time axis on this tab, so the review-cursor contract does not apply.
        _ = context;
        _sequenceRenderer.RenderFrame(frame);
    }
}
