using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

internal sealed class LongTermPerfFrameConsumer : IAnalysisFrameConsumer, IThemedFrameConsumer
{
    private readonly LongTermPerfRenderer _renderer;

    public LongTermPerfFrameConsumer(LongTermPerfRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.LongTermPerfTabId;

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
        // History is cumulative on the frame; nothing to accumulate UI-side.
        _ = frame;
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _renderer.RenderFrame(frame, context);
    }
}
