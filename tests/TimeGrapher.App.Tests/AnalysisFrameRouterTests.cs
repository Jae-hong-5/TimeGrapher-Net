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

        router.Route(new AnalysisFrame(), InfoTabCatalog.SoundPrintTabId, new AnalysisTabRenderContext(48000));

        Assert.Equal(0, rateScope.RenderCount);
        Assert.Equal(1, soundPrint.RenderCount);
        Assert.Equal(1, rateScope.ObserveCount);
        Assert.Equal(1, soundPrint.ObserveCount);
    }

    [Fact]
    public void RenderToAllRendersEveryConsumerForCursorClear()
    {
        var rateScope = new FakeConsumer(InfoTabCatalog.RateScopeTabId);
        var soundPrint = new FakeConsumer(InfoTabCatalog.SoundPrintTabId);
        var router = new AnalysisFrameRouter(new IAnalysisFrameConsumer[] { rateScope, soundPrint });

        router.RenderToAll(new AnalysisFrame(), new AnalysisTabRenderContext(48000, ReviewCursorTimeS: null));

        Assert.Equal(1, rateScope.RenderCount);
        Assert.Equal(1, soundPrint.RenderCount);
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
            _ = frame;
            ObserveCount++;
        }

        public void RenderFrame(AnalysisFrame frame, AnalysisTabRenderContext context)
        {
            _ = frame;
            _ = context;
            RenderCount++;
        }
    }
}
