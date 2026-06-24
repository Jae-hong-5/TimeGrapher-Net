using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class RunCommandServiceTests
{
    [Fact]
    public async Task StartIgnoresRequestWhileClosing()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations
        {
            IsClosing = true,
            CurrentMode = RunCommandMode.Live,
        };
        var service = new RunCommandService(vm, operations);

        await service.StartAsync();

        Assert.Empty(operations.Calls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task StartLiveConfiguresAudioBeforeStarting()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
        };
        operations.StartLiveAsyncImpl = () =>
        {
            vm.SetRunning();
            vm.StatusText = "Running";
            return Task.FromResult(true);
        };
        var service = new RunCommandService(vm, operations);

        await service.StartAsync();

        Assert.Equal(new[] { "ConfigureLiveAudio", "StartLiveAsync" }, operations.Calls);
        Assert.Equal(RunUiState.Running, vm.RunState);
        Assert.Equal("Running", vm.StatusText);
    }

    [Fact]
    public async Task StartFailureCleansUpAndShowsError()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Playback,
        };
        operations.StartPlaybackAsyncImpl = () => throw new InvalidOperationException("bad file");
        var service = new RunCommandService(vm, operations);

        await service.StartAsync();

        Assert.Equal(new[] { "StartPlaybackAsync", "CleanupFailedStart", "ShowStartFailureAsync" }, operations.Calls);
        Assert.Equal(new[] { "bad file" }, operations.StartFailureMessages);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal(UserErrorMessages.CouldNotStartRun, vm.StatusText);
    }

    [Fact]
    public void TogglePauseNoOpsWithoutActiveWorker()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        var operations = new FakeRunCommandOperations
        {
            HasActiveWorker = false,
        };
        var service = new RunCommandService(vm, operations);

        service.TogglePause();

        Assert.Empty(operations.PauseValues);
        Assert.Equal(RunUiState.Running, vm.RunState);
    }

    [Fact]
    public void TogglePausePausesAndResumesActiveWorker()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        var operations = new FakeRunCommandOperations
        {
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.TogglePause();
        service.TogglePause();

        Assert.Equal(new[] { true, false }, operations.PauseValues);
        Assert.Equal(RunUiState.Running, vm.RunState);
        Assert.Equal("Running", vm.StatusText);
    }

    [Fact]
    public void TogglePauseUsesViewModelStateAsSourceOfTruth()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        var operations = new FakeRunCommandOperations
        {
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.TogglePause();
        vm.SetRunning();
        service.TogglePause();

        Assert.Equal(new[] { true, true }, operations.PauseValues);
        Assert.Equal(RunUiState.Paused, vm.RunState);
        Assert.Equal("Paused", vm.StatusText);
    }

    [Fact]
    public void PauseIfRunningPausesWithoutResumingPausedRuns()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        var operations = new FakeRunCommandOperations
        {
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.PauseIfRunning();
        service.PauseIfRunning();

        Assert.Equal(new[] { true }, operations.PauseValues);
        Assert.Equal(RunUiState.Paused, vm.RunState);
        Assert.Equal("Paused", vm.StatusText);
    }

    [Fact]
    public void PauseIfRunningIgnoresNonRunningStates()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetPaused();
        var operations = new FakeRunCommandOperations
        {
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.PauseIfRunning();
        vm.SetStopped();
        service.PauseIfRunning();

        Assert.Empty(operations.PauseValues);
    }

    [Fact]
    public void StopPlaybackStopsClosesInvalidatesAndKeepsPlaybackSelection()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Playback,
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(new[] { false }, operations.PauseValues);
        Assert.Equal(1, operations.StopPlaybackCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(0, operations.RestorePlaybackOrSimulationAudioStateCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Stopped", vm.StatusText);
        Assert.False(vm.IsSampleRateEnabled);
    }

    [Fact]
    public void StopClearsAwaitingBeatSyncFlag()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.IsAwaitingBeatSync = true;
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Playback,
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.False(vm.IsAwaitingBeatSync);
    }

    [Fact]
    public async Task StartSetsAwaitingBeatSyncFlag()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
        };
        operations.StartLiveAsyncImpl = () =>
        {
            vm.SetRunning();
            return Task.FromResult(true);
        };
        var service = new RunCommandService(vm, operations);

        await service.StartAsync();

        Assert.True(vm.IsAwaitingBeatSync);
    }

    [Fact]
    public void StopSimulationKeepsSimulationSelectionAndGainDisabled()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Simulation,
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(1, operations.StopSimulationCalls);
        Assert.Equal(0, operations.RestorePlaybackOrSimulationAudioStateCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.True(vm.IsSampleRateEnabled);
        Assert.False(vm.IsGainEnabled);
    }

    [Theory]
    [InlineData((int)RunCommandMode.Playback, 2, "Playback")]
    [InlineData((int)RunCommandMode.Simulation, 3, "Simulation")]
    [InlineData((int)RunCommandMode.Live, 1, "Live: Mic B")]
    public void StopWithoutResetKeepsSelectedInputDevice(
        int mode,
        int selectedIndex,
        string expectedSelection)
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetInputDeviceNames(new[] { "Live: Mic A", "Live: Mic B", "Playback", "Simulation" });
        vm.SelectedInputDeviceIndex = selectedIndex;
        vm.SetRunning();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = (RunCommandMode)mode,
            RestorePlaybackOrSimulationAudioStateAction = () => vm.SelectedInputDeviceIndex = 0,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(
            expectedSelection,
            MainWindowSelectionCoordinator.ItemText(vm.InputDeviceNames, vm.SelectedInputDeviceIndex));
        Assert.Equal(0, operations.ResetRunStateCalls);
        Assert.Equal(0, operations.RefreshDevicesCalls);
    }

    [Fact]
    public void StopWithStoppingOutcomeEntersStopFailedRecovery()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.True(vm.ResetCommand.CanExecute(null));
        Assert.Equal(UserErrorMessages.StopDidNotFinish, vm.StatusText);
        Assert.Equal(0, operations.CloseAudioCalls);
        Assert.Equal(0, operations.InvalidateRunSessionCalls);
    }

    [Fact]
    public void StopFailureMessagePointsAtTheVisibleStopRetry()
    {
        Assert.Contains("Stop", UserErrorMessages.StopDidNotFinish, StringComparison.Ordinal);
        Assert.DoesNotContain("Reset", UserErrorMessages.StopDidNotFinish, StringComparison.Ordinal);
    }

    [Fact]
    public void StopOverwritesThroughputStatusWithStopping()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Backgroud Audio Thread Average - FPS: 12.3";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.Equal(UserErrorMessages.StopDidNotFinish, vm.StatusText);
    }

    [Fact]
    public void StopRetryAfterWorkerTimeoutCompletesStop()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.Equal(0, operations.CloseAudioCalls);

        operations.StopLiveOutcome = RunCommandStopOutcome.Stopped;
        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Stopped", vm.StatusText);
        // The retry re-issues StopMode; re-issuing must remain safe (idempotent).
        Assert.Equal(2, operations.StopLiveCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
    }

    [Fact]
    public void StopRetryAfterFailedAudioCloseRetriesClose()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            CloseAudioResult = false,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(0, operations.InvalidateRunSessionCalls);

        operations.CloseAudioResult = true;
        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Stopped", vm.StatusText);
        // The retry re-issues StopMode; re-issuing must remain safe (idempotent).
        Assert.Equal(2, operations.StopLiveCalls);
        Assert.Equal(2, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
    }

    [Fact]
    public void StopIgnoresRequestWhenAlreadyStopped()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunWithoutReset();

        Assert.Empty(operations.Calls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
    }

    [Fact]
    public void StopAndRefreshDevicesStopsLiveWithoutResettingRunState()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            HasActiveWorker = true,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunAndRefreshDevices();

        Assert.Equal(1, operations.StopLiveCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(1, operations.RefreshDevicesCalls);
        Assert.Equal(0, operations.ResetRunStateCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Stopped", vm.StatusText);
    }

    [Fact]
    public void StopAndRefreshDevicesWaitsUntilStopCompletesBeforeRefreshing()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetRunning();
        vm.StatusText = "Running";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.StopRunAndRefreshDevices();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.Equal(0, operations.RefreshDevicesCalls);
        Assert.Equal(0, operations.InvalidateRunSessionCalls);

        operations.StopLiveOutcome = RunCommandStopOutcome.Stopped;
        service.StopRunWithoutReset();

        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal(1, operations.RefreshDevicesCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(0, operations.ResetRunStateCalls);
    }

    [Fact]
    public void ResetWhileStoppedClearsRunStateAndRefreshesDevices()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeRunCommandOperations();
        var service = new RunCommandService(vm, operations);

        service.Reset();

        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Reset", vm.StatusText);
        Assert.Equal(1, operations.ResetRunStateCalls);
        Assert.Equal(1, operations.RefreshDevicesCalls);
        Assert.Equal(0, operations.StopLiveCalls);
        Assert.Equal(0, operations.InvalidateRunSessionCalls);
    }

    [Fact]
    public void ResetFromPausedStopsThenClearsRunStateAndRefreshesDevices()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetPaused();
        vm.StatusText = "Paused";
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
        };
        var service = new RunCommandService(vm, operations);

        service.Reset();

        Assert.Equal(new[] { false }, operations.PauseValues);
        Assert.Equal(1, operations.StopLiveCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(1, operations.ResetRunStateCalls);
        Assert.Equal(1, operations.RefreshDevicesCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Reset", vm.StatusText);
    }

    [Fact]
    public void ResetFromPausedFailedStopDoesNotResetAndKeepsRetryAvailable()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetPaused();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.Reset();

        Assert.Equal(RunUiState.StopFailed, vm.RunState);
        Assert.True(vm.ResetCommand.CanExecute(null));
        Assert.Equal(0, operations.ResetRunStateCalls);
        Assert.Equal(0, operations.RefreshDevicesCalls);
        Assert.Equal(0, operations.InvalidateRunSessionCalls);
    }

    [Fact]
    public void ResetRetryAfterFailedPausedStopCompletesThePendingReset()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetPaused();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.Reset();
        operations.StopLiveOutcome = RunCommandStopOutcome.Stopped;
        service.Reset();

        Assert.Equal(2, operations.StopLiveCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(1, operations.ResetRunStateCalls);
        Assert.Equal(1, operations.RefreshDevicesCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Reset", vm.StatusText);
    }

    [Fact]
    public void StopRetryAfterFailedPausedResetDoesNotCompleteThePendingReset()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetPaused();
        var operations = new FakeRunCommandOperations
        {
            CurrentMode = RunCommandMode.Live,
            StopLiveOutcome = RunCommandStopOutcome.Stopping,
        };
        var service = new RunCommandService(vm, operations);

        service.Reset();
        operations.StopLiveOutcome = RunCommandStopOutcome.Stopped;
        service.StopRunWithoutReset();

        Assert.Equal(2, operations.StopLiveCalls);
        Assert.Equal(1, operations.CloseAudioCalls);
        Assert.Equal(1, operations.InvalidateRunSessionCalls);
        Assert.Equal(0, operations.ResetRunStateCalls);
        Assert.Equal(0, operations.RefreshDevicesCalls);
        Assert.Equal(RunUiState.Stopped, vm.RunState);
        Assert.Equal("Stopped", vm.StatusText);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel();
    }

    private sealed class FakeRunCommandOperations : IRunCommandOperations
    {
        public List<string> Calls { get; } = new();

        public List<bool> PauseValues { get; } = new();

        public List<string> StartFailureMessages { get; } = new();

        public bool IsClosing { get; set; }

        public bool HasActiveWorker { get; set; }

        public RunCommandMode CurrentMode { get; set; } = RunCommandMode.Live;

        public Func<Task<bool>> StartLiveAsyncImpl { get; set; } = () => Task.FromResult(true);

        public Func<Task<bool>> StartPlaybackAsyncImpl { get; set; } = () => Task.FromResult(true);

        public Func<Task<bool>> StartSimulationAsyncImpl { get; set; } = () => Task.FromResult(true);

        public Action? RestorePlaybackOrSimulationAudioStateAction { get; set; }

        public RunCommandStopOutcome StopLiveOutcome { get; set; } = RunCommandStopOutcome.Stopped;

        public RunCommandStopOutcome StopPlaybackOutcome { get; set; } = RunCommandStopOutcome.Stopped;

        public RunCommandStopOutcome StopSimulationOutcome { get; set; } = RunCommandStopOutcome.Stopped;

        public bool CloseAudioResult { get; set; } = true;

        public int StopLiveCalls { get; private set; }

        public int StopPlaybackCalls { get; private set; }

        public int StopSimulationCalls { get; private set; }

        public int CloseAudioCalls { get; private set; }

        public int InvalidateRunSessionCalls { get; private set; }

        public int RestorePlaybackOrSimulationAudioStateCalls { get; private set; }

        public int ResetRunStateCalls { get; private set; }

        public int RefreshDevicesCalls { get; private set; }

        public void ConfigureLiveAudio()
        {
            Calls.Add("ConfigureLiveAudio");
        }

        public Task<bool> StartLiveAsync()
        {
            Calls.Add("StartLiveAsync");
            return StartLiveAsyncImpl();
        }

        public Task<bool> StartPlaybackAsync()
        {
            Calls.Add("StartPlaybackAsync");
            return StartPlaybackAsyncImpl();
        }

        public Task<bool> StartSimulationAsync()
        {
            Calls.Add("StartSimulationAsync");
            return StartSimulationAsyncImpl();
        }

        public void SetWorkersPaused(bool paused)
        {
            Calls.Add("SetWorkersPaused:" + paused);
            PauseValues.Add(paused);
        }

        public void CleanupFailedStart()
        {
            Calls.Add("CleanupFailedStart");
        }

        public Task ShowStartFailureAsync(Exception exception)
        {
            Calls.Add("ShowStartFailureAsync");
            StartFailureMessages.Add(exception.Message);
            return Task.CompletedTask;
        }

        public RunCommandStopOutcome StopLive()
        {
            Calls.Add("StopLive");
            StopLiveCalls++;
            return StopLiveOutcome;
        }

        public RunCommandStopOutcome StopPlayback()
        {
            Calls.Add("StopPlayback");
            StopPlaybackCalls++;
            return StopPlaybackOutcome;
        }

        public RunCommandStopOutcome StopSimulation()
        {
            Calls.Add("StopSimulation");
            StopSimulationCalls++;
            return StopSimulationOutcome;
        }

        public bool CloseAudio()
        {
            Calls.Add("CloseAudio");
            CloseAudioCalls++;
            return CloseAudioResult;
        }

        public void InvalidateRunSession()
        {
            Calls.Add("InvalidateRunSession");
            InvalidateRunSessionCalls++;
        }

        public void RestorePlaybackOrSimulationAudioState()
        {
            Calls.Add("RestorePlaybackOrSimulationAudioState");
            RestorePlaybackOrSimulationAudioStateCalls++;
            RestorePlaybackOrSimulationAudioStateAction?.Invoke();
        }

        public void ResetRunState()
        {
            Calls.Add("ResetRunState");
            ResetRunStateCalls++;
        }

        public void RefreshDevices()
        {
            Calls.Add("RefreshDevices");
            RefreshDevicesCalls++;
        }
    }
}
