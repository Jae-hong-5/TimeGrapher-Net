using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Tabs;

internal sealed class AnalysisFrameRouter
{
    private readonly Dictionary<string, IAnalysisFrameConsumer> _consumers;

    public AnalysisFrameRouter(IEnumerable<IAnalysisFrameConsumer> consumers)
    {
        _consumers = consumers.ToDictionary(consumer => consumer.TabId, StringComparer.Ordinal);
    }

    public bool HasConsumer(string tabId)
    {
        return _consumers.ContainsKey(tabId);
    }

    public void Route(AnalysisFrame frame, string activeTabId, AnalysisTabRenderContext context)
    {
        foreach (IAnalysisFrameConsumer consumer in _consumers.Values)
        {
            consumer.ObserveFrame(frame);
        }

        if (_consumers.TryGetValue(activeTabId, out IAnalysisFrameConsumer? activeConsumer))
        {
            activeConsumer.RenderFrame(frame, context);
        }
    }

    /// <summary>
    /// One-shot render fan-out for the pause-exit cursor clear: every tab that
    /// drew the dotted review cursor during a scrubbed pause must re-render
    /// without it, not just the active one — after a stop the kept frame is
    /// invalidated, so a later tab switch can never heal an inactive tab's
    /// stale cursor. Render-only: the kept frame was already observed by all
    /// consumers when it was first routed. Per-frame routing stays single-tab
    /// (<see cref="Route"/>), so the schedule-resources tactic is untouched.
    /// </summary>
    public void RenderToAll(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        foreach (IAnalysisFrameConsumer consumer in _consumers.Values)
        {
            consumer.RenderFrame(frame, context);
        }
    }
}
