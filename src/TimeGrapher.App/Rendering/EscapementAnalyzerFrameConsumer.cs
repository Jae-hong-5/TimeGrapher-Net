using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class EscapementAnalyzerFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly EscapementAnalyzerRenderer _renderer;

    public EscapementAnalyzerFrameConsumer(EscapementAnalyzerRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.EscapementAnalyzerTabId;

    public void ApplyTheme(PlotThemePalette theme)
    {
        _renderer.ApplyTheme(theme);
    }

    public void Initialize(AnalysisTabResetContext context)
    {
        _renderer.CreateGraphs();
    }

    public void Reset(AnalysisTabResetContext context)
    {
        _renderer.Reset();
    }

    public void ObserveFrame(AnalysisFrame frame)
    {
        // The repeatability tracker must see every routed frame, not just the
        // ones rendered while this tab is active - otherwise the advertised
        // last-32-beats window silently dropped every beat that arrived while
        // another tab was selected. Version-gated, so the cost per frame is a
        // ulong compare (and a small ring append ~2x per second).
        _renderer.ObserveSegments(frame);
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _renderer.RenderFrame(frame, context);
    }
}
