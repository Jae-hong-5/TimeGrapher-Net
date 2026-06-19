using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// Stop-path contract of the run-session controller's input-worker teardown:
/// completion handlers must be detached before the bounded TryStop (so a late
/// completion cannot bypass the stop/reset intent), including when TryStop times
/// out. The analysis-thread side creates a concrete AnalysisWorker internally and
/// is exercised through the run-lifecycle integration paths instead.
/// </summary>
public sealed class RunSessionControllerTests
{
    private static RunSessionController NewController(
        List<string>? statuses = null,
        FakeUserErrorLog? errorLog = null) =>
        new(
            createAnalysisConfig: _ => throw new System.NotSupportedException("not used in these tests"),
            resetBeforeRun: () => { },
            clearPendingFrames: () => { },
            resetRenderTiming: () => { },
            onAnalysisFrameReady: _ => { },
            setStatus: status => statuses?.Add(status),
            errorLog);

    [Fact]
    public void StopInputWorkerDetachesCompletionBeforeTryStop()
    {
        var controller = NewController();
        var order = new List<string>();
        var worker = new FakeInputWorker(order) { StopResult = true };
        controller.AttachInputWorker(worker, runSessionToken: 1, () => order.Add("detach-completion"));

        RunSessionStopOutcome outcome = controller.StopInputWorker("Test");

        Assert.Equal(RunSessionStopOutcome.Stopped, outcome);
        int detachIdx = order.IndexOf("detach-completion");
        int tryStopIdx = order.IndexOf("trystop");
        Assert.True(
            detachIdx >= 0 && tryStopIdx >= 0 && detachIdx < tryStopIdx,
            "completion handlers must detach before TryStop; order: " + string.Join(",", order));
    }

    [Fact]
    public void StopInputWorkerDetachesCompletionEvenWhenTryStopTimesOut()
    {
        var statuses = new List<string>();
        var errorLog = new FakeUserErrorLog();
        var controller = NewController(statuses, errorLog);
        var order = new List<string>();
        var worker = new FakeInputWorker(order) { StopResult = false };
        controller.AttachInputWorker(worker, runSessionToken: 1, () => order.Add("detach-completion"));

        RunSessionStopOutcome outcome = controller.StopInputWorker("Test");

        // On timeout the worker is kept for retry, but the completion handler must
        // already be detached so a late completion cannot re-enter the lifecycle.
        Assert.Equal(RunSessionStopOutcome.Stopping, outcome);
        int detachIdx = order.IndexOf("detach-completion");
        int tryStopIdx = order.IndexOf("trystop");
        Assert.True(
            detachIdx >= 0 && detachIdx < tryStopIdx,
            "completion must detach before a timed-out TryStop; order: " + string.Join(",", order));
        Assert.DoesNotContain("dispose", order);
        Assert.Equal(new[] { UserErrorMessages.StopDidNotFinish }, statuses);
        var entry = Assert.Single(errorLog.Entries);
        Assert.Equal(UserErrorMessages.StopDidNotFinish, entry.UserMessage);
        Assert.Equal("Test worker did not stop within 2000 ms.", entry.Detail);
    }

    private sealed class FakeInputWorker : IAudioInputWorker
    {
        private readonly List<string> _order;

        public FakeInputWorker(List<string> order) => _order = order;

        public bool StopResult { get; set; } = true;

        public event Action? DataReady;

        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;

        public bool TryStop(TimeSpan timeout)
        {
            _order.Add("trystop");
            return StopResult;
        }

        public void Dispose() => _order.Add("dispose");

        // Keeps the interface event from tripping CS0067 under warnings-as-errors.
        public void RaiseDataReady() => DataReady?.Invoke();
    }
}
