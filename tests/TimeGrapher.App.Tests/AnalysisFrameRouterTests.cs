using System.Linq;
using TimeGrapher.App.Tabs;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AnalysisFrameRouterTests
{
    [Fact]
    public void RouteObservesAllConsumersAndRendersOnlyActiveTab()
    {
        var rateScope = new FakeConsumer(InfoTabCatalog.RateScopeTabId);
        var soundPrint = new FakeConsumer(InfoTabCatalog.SoundPrintTabId);
        var router = new AnalysisFrameRouter(new IAnalysisFrameConsumer[] { rateScope, soundPrint });

        var frame = new AnalysisFrame();
        var context = new AnalysisTabRenderContext(48000);
        router.Route(frame, InfoTabCatalog.SoundPrintTabId, context);

        Assert.Equal(0, rateScope.RenderCount);
        Assert.Equal(1, soundPrint.RenderCount);
        Assert.Equal(1, rateScope.ObserveCount);
        Assert.Equal(1, soundPrint.ObserveCount);

        // The router must pass the routed frame/context THROUGH unchanged, not merely
        // invoke the callbacks: every consumer observes the same frame instance, and
        // the active tab renders that same frame with the same context.
        Assert.Same(frame, rateScope.LastObservedFrame);
        Assert.Same(frame, soundPrint.LastObservedFrame);
        Assert.Same(frame, soundPrint.LastRenderedFrame);
        Assert.Same(context, soundPrint.LastRenderContext);
        Assert.Null(rateScope.LastRenderedFrame); // inactive tab did not render
    }

    [Fact]
    public void RenderToAllRendersEveryConsumerForCursorClear()
    {
        var rateScope = new FakeConsumer(InfoTabCatalog.RateScopeTabId);
        var soundPrint = new FakeConsumer(InfoTabCatalog.SoundPrintTabId);
        var router = new AnalysisFrameRouter(new IAnalysisFrameConsumer[] { rateScope, soundPrint });

        var frame = new AnalysisFrame();
        var context = new AnalysisTabRenderContext(48000, ReviewCursorTimeS: null);
        router.RenderToAll(frame, context);

        Assert.Equal(1, rateScope.RenderCount);
        Assert.Equal(1, soundPrint.RenderCount);
        Assert.Same(frame, rateScope.LastRenderedFrame);
        Assert.Same(frame, soundPrint.LastRenderedFrame);
        Assert.Same(context, soundPrint.LastRenderContext);
    }

    [Fact]
    public void RouterReportsRegisteredTabConsumers()
    {
        var router = new AnalysisFrameRouter(
            InfoTabCatalog.All.Select(tab => (IAnalysisFrameConsumer)new FakeConsumer(tab.Id)).ToArray());

        Assert.All(InfoTabCatalog.All, tab => Assert.True(router.HasConsumer(tab.Id)));
        Assert.False(router.HasConsumer("missing"));
    }

    private sealed class FakeConsumer : IAnalysisFrameConsumer
    {
        public FakeConsumer(string tabId)
        {
            TabId = tabId;
        }

        public string TabId { get; }
        public int InitializeCount { get; private set; }
        public int ResetCount { get; private set; }
        public int ObserveCount { get; private set; }
        public int RenderCount { get; private set; }
        public AnalysisFrame? LastObservedFrame { get; private set; }
        public AnalysisFrame? LastRenderedFrame { get; private set; }
        public AnalysisTabRenderContext? LastRenderContext { get; private set; }

        public void Initialize(AnalysisTabResetContext context)
        {
            _ = context;
            InitializeCount++;
        }

        public void Reset(AnalysisTabResetContext context)
        {
            _ = context;
            ResetCount++;
        }

        public void ObserveFrame(AnalysisFrame frame)
        {
            LastObservedFrame = frame;
            ObserveCount++;
        }

        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
        {
            LastRenderedFrame = frame;
            LastRenderContext = context;
            RenderCount++;
        }
    }
}
