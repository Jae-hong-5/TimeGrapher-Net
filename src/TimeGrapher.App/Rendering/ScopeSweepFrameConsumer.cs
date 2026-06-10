using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class ScopeSweepFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly ScopeSweepRenderer _renderer;

    public ScopeSweepFrameConsumer(ScopeSweepRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.ScopeSweepTabId;

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
        // The sweep window accumulates in Core and rides the frame as a replace
        // snapshot; nothing to accumulate UI-side.
        _ = frame;
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _renderer.RenderFrame(frame, context);
    }
}
