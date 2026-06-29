using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.Core.Tests;

/// <summary>
/// WorkerPauseGate: the shared pause/resume barrier the capture/playback/sim
/// workers block on. It passes straight through when not paused, parks the
/// caller while paused until resumed from another thread, and returns false
/// (do-not-continue) when the worker is cancelled while still paused.
/// </summary>
public sealed class WorkerPauseGateTests
{
    [Fact]
    public void WaitWhilePaused_ReturnsImmediately_WhenNotPaused()
    {
        using var gate = new WorkerPauseGate();

        Assert.False(gate.IsPaused);
        Assert.True(gate.WaitWhilePaused(() => false));
    }

    [Fact]
    public async Task WaitWhilePaused_BlocksUntilResumedFromAnotherThread()
    {
        using var gate = new WorkerPauseGate();
        gate.SetPaused(true);

        Task<bool> waiter = Task.Run(() => gate.WaitWhilePaused(() => false));

        // While paused the waiter must not complete.
        Task completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(waiter, completed);

        gate.SetPaused(false);

        completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waiter, completed);
        Assert.True(await waiter);
    }

    [Fact]
    public async Task WaitWhilePaused_ReturnsFalse_WhenCancelledWhilePaused()
    {
        using var gate = new WorkerPauseGate();
        gate.SetPaused(true);
        using var cancelled = new ManualResetEventSlim(initialState: false);

        Task<bool> waiter = Task.Run(() => gate.WaitWhilePaused(() => cancelled.IsSet));

        Task completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(waiter, completed);

        // Cancellation while still paused must break the wait and report do-not-continue.
        cancelled.Set();

        completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waiter, completed);
        Assert.False(await waiter);
    }
}
