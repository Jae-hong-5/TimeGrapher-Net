using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// AnalysisFramePresenter maps a rendered frame onto view-model state (the non-rendering half of the
/// MainWindow's frame handling). These lock the view-model-facing behavior it owns: the
/// awaiting-beat-sync gate (skipped once Stopped so a drained final frame can't re-raise the overlay),
/// the review maximum growth, and the reset of latency/review state.
/// </summary>
public sealed class AnalysisFramePresenterTests
{
    private sealed class RecordingErrorLog : IUserErrorLog
    {
        public int Writes { get; private set; }
        public string? LastMessage { get; private set; }
        public void Write(string message, string detail) { Writes++; LastMessage = message; }
    }

    private static AnalysisFramePresenter Create(MainWindowViewModel vm) =>
        new(vm, NullUserErrorLog.Instance);

    [Fact]
    public void Present_WhileRunning_SetsAwaitingBeatSyncFromFrame()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        AnalysisFramePresenter presenter = Create(vm);

        presenter.Present(new AnalysisFrame { BeatSynced = false }, droppedFrames: 0, displayTicks: 0, sampleRate: 48000);
        Assert.True(vm.IsAwaitingBeatSync);

        presenter.Present(new AnalysisFrame { BeatSynced = true }, droppedFrames: 0, displayTicks: 0, sampleRate: 48000);
        Assert.False(vm.IsAwaitingBeatSync);
    }

    [Fact]
    public void Present_WhileStopped_DoesNotReRaiseAwaitingBeatSync()
    {
        var vm = new MainWindowViewModel();
        AnalysisFramePresenter presenter = Create(vm);
        vm.SetStopped();
        vm.IsAwaitingBeatSync = false; // the run-command service clears it on stop

        // The drained final frame arrives after Stopped (the session id is still alive); it must
        // not re-raise the waiting overlay.
        presenter.Present(new AnalysisFrame { BeatSynced = false }, 0, 0, 48000);

        Assert.False(vm.IsAwaitingBeatSync);
    }

    [Fact]
    public void Present_GrowsReviewMaximumFromHistory()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        AnalysisFramePresenter presenter = Create(vm);

        presenter.Present(
            new AnalysisFrame { MetricsHistory = new BeatMetricsHistorySnapshot { LatestTimeS = 5.0 } },
            0, 0, 48000);

        Assert.Equal(5.0, vm.ReviewMaximumS);
    }

    [Fact]
    public void Present_InputOverrun_SetsStatusAndLogsDetail()
    {
        var vm = new MainWindowViewModel();
        var errorLog = new RecordingErrorLog();
        var presenter = new AnalysisFramePresenter(vm, errorLog);

        presenter.Present(new AnalysisFrame { InputOverrun = true }, 0, 0, 48000);

        Assert.Equal(UserErrorMessages.AudioInputInterrupted, vm.StatusText);
        Assert.Equal(1, errorLog.Writes);
    }

    [Fact]
    public void Present_SetsLatencyText_FromTimestampedFrame()
    {
        var vm = new MainWindowViewModel();
        AnalysisFramePresenter presenter = Create(vm);

        // A frame with both timestamps feeds the latency accumulator, so the first status format
        // (no prior status tick) returns a readout.
        presenter.Present(
            new AnalysisFrame { CaptureTimestamp = 1000, ProcessingCompletedTimestamp = 2000 },
            droppedFrames: 0, displayTicks: 3000, sampleRate: 48000);

        Assert.NotEqual("", vm.LatencyText);
    }

    [Fact]
    public void Present_ThroughputDedups_AndResetReEmits()
    {
        var vm = new MainWindowViewModel();
        AnalysisFramePresenter presenter = Create(vm);

        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Contains("Backgroud", vm.StatusText); // throughput line emitted

        // The reporter only re-emits on change: an identical frame leaves the status untouched.
        vm.StatusText = "marker";
        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Equal("marker", vm.StatusText);

        // Reset must clear the reporter's remembered rates, so the same frame re-emits afterwards.
        presenter.Reset();
        vm.StatusText = "marker";
        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Contains("Backgroud", vm.StatusText);
    }

    [Fact]
    public void Reset_ClearsLatencyAndReview()
    {
        var vm = new MainWindowViewModel();
        AnalysisFramePresenter presenter = Create(vm);
        vm.LatencyText = "63 ms";
        vm.UpdateReviewMaximum(10.0);

        presenter.Reset();

        Assert.Equal("", vm.LatencyText);
        Assert.Equal(0.0, vm.ReviewMaximumS);
    }
}
