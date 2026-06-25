using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Rendering;

/// <summary>
/// Routes frames to the Watch Health radar renderer. The radar reads the cumulative
/// per-position snapshot on the active tab only, so it renders in
/// <see cref="RenderFrame"/> (no time axis, so the review-cursor context is unused).
/// It also participates in the accept-band broadcast so a Settings edit moves the
/// healthy ring immediately, even while stopped.
/// </summary>
internal sealed class WatchHealthRadarFrameConsumer : IAnalysisFrameConsumer, IAcceptBandConsumer
{
    private readonly WatchHealthRadarRenderer _renderer;

    public WatchHealthRadarFrameConsumer(WatchHealthRadarRenderer renderer)
    {
        _renderer = renderer;
    }

    public string TabId => InfoTabCatalog.WatchHealthRadarTabId;

    public void Initialize(AnalysisTabResetContext context) => _renderer.Reset();

    public void Reset(AnalysisTabResetContext context) => _renderer.Reset();

    public void ObserveFrame(AnalysisFrame frame)
    {
    }

    public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
    {
        _ = context;
        _renderer.RenderFrame(frame);
    }

    public void ApplyAcceptBands() => _renderer.ApplyAcceptBands();
}
