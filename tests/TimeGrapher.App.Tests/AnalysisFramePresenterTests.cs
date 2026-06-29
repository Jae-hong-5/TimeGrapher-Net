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
    public void Present_WhileStopped_DoesNotOverwriteTerminalStatusOrLatency()
    {
        var vm = new MainWindowViewModel();
        var errorLog = new RecordingErrorLog();
        var presenter = new AnalysisFramePresenter(vm, errorLog);

        // The stop already set the terminal status and latency readout.
        vm.SetStopped();
        vm.StatusText = "Stopped";
        vm.LatencyText = "final";

        // A drained final frame arriving after Stopped carries a warning (overrun) and
        // a timestamped latency leg; neither must overwrite the terminal readouts, and
        // it must not write to the error log.
        presenter.Present(
            new AnalysisFrame
            {
                InputOverrun = true,
                CaptureTimestamp = 1000,
                ProcessingCompletedTimestamp = 2000,
            },
            droppedFrames: 0, displayTicks: 3000, sampleRate: 48000);

        Assert.Equal("Stopped", vm.StatusText);
        Assert.Equal("final", vm.LatencyText);
        Assert.Equal(0, errorLog.Writes);
    }

    [Fact]
    public void Present_WhileStopped_StillGrowsReviewMaximum()
    {
        var vm = new MainWindowViewModel();
        AnalysisFramePresenter presenter = Create(vm);
        vm.SetStopped();

        // The review-range growth is the stopped-safe update that survives the gate.
        presenter.Present(
            new AnalysisFrame { MetricsHistory = new BeatMetricsHistorySnapshot { LatestTimeS = 7.0 } },
            0, 0, 48000);

        Assert.Equal(7.0, vm.ReviewMaximumS);
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
        vm.SetRunning();
        var errorLog = new RecordingErrorLog();
        var presenter = new AnalysisFramePresenter(vm, errorLog);

        presenter.Present(new AnalysisFrame { InputOverrun = true }, 0, 0, 48000);

        Assert.Equal(UserErrorMessages.AudioInputInterrupted, vm.StatusText);
        Assert.Equal(1, errorLog.Writes);
    }

    [Fact]
    public void Present_RepeatedIdenticalWarning_LogsDetailOnce_AndChangedDetailLogsAgain()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        var errorLog = new RecordingErrorLog();
        var presenter = new AnalysisFramePresenter(vm, errorLog);

        // A persistent warning state reports the same LogDetail every active-tab frame.
        // The presenter must write it once (per state-change), not once per frame.
        var overrun = new AnalysisFrame { InputOverrun = true, InputSamplesDropped = 96 };
        presenter.Present(overrun, 0, 0, 48000);
        presenter.Present(overrun, 0, 0, 48000);
        presenter.Present(overrun, 0, 0, 48000);
        Assert.Equal(1, errorLog.Writes);

        // A changed detail (different dropped-sample count) is a new state and logs again.
        presenter.Present(new AnalysisFrame { InputOverrun = true, InputSamplesDropped = 200 }, 0, 0, 48000);
        Assert.Equal(2, errorLog.Writes);
    }

    [Fact]
    public void Reset_ReEmitsAPersistentWarningDetail()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        var errorLog = new RecordingErrorLog();
        var presenter = new AnalysisFramePresenter(vm, errorLog);

        var overrun = new AnalysisFrame { InputOverrun = true, InputSamplesDropped = 96 };
        presenter.Present(overrun, 0, 0, 48000);
        Assert.Equal(1, errorLog.Writes);

        // A new session must re-log the same persistent detail (the dedup memory clears).
        presenter.Reset();
        presenter.Present(overrun, 0, 0, 48000);
        Assert.Equal(2, errorLog.Writes);
    }

    [Fact]
    public void Present_SetsLatencyText_FromTimestampedFrame()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        AnalysisFramePresenter presenter = Create(vm);

        // A frame with both timestamps feeds the latency accumulator, so the first status format
        // (no prior status tick) returns a readout. Seed nonzero drop/miss/sync-loss counts so the
        // readout carries every stable field of LatencyStatsTracker.FormatStatus, not just "non-empty".
        presenter.Present(
            new AnalysisFrame
            {
                CaptureTimestamp = 1000,
                ProcessingCompletedTimestamp = 2000,
                InputSamplesDropped = 7,
                MissedBeats = 3,
                SyncLossCount = 2,
            },
            droppedFrames: 5, displayTicks: 3000, sampleRate: 48000);

        // Format: "E2E {e2e} ms | drop {drop} smp | miss {miss} | sync−loss {sync} | {frm} frm"
        Assert.Contains("E2E", vm.LatencyText);
        Assert.Contains("ms", vm.LatencyText);
        Assert.Contains("drop 7 smp", vm.LatencyText);
        Assert.Contains("miss 3", vm.LatencyText);
        Assert.Contains("sync−loss 2", vm.LatencyText);
        Assert.Contains("5 frm", vm.LatencyText);
    }

    [Fact]
    public void Present_ThroughputDedups_AndResetReEmits()
    {
        var vm = new MainWindowViewModel();
        vm.SetRunning();
        AnalysisFramePresenter presenter = Create(vm);

        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Contains("BG", vm.StatusText); // throughput line emitted

        // The reporter only re-emits on change: an identical frame leaves the status untouched.
        vm.StatusText = "marker";
        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Equal("marker", vm.StatusText);

        // Reset must clear the reporter's remembered rates, so the same frame re-emits afterwards.
        presenter.Reset();
        vm.StatusText = "marker";
        presenter.Present(new AnalysisFrame { BackgroundFps = 30 }, 0, 0, 48000);
        Assert.Contains("BG", vm.StatusText);
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
