using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed partial class RunCommandService
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IRunCommandOperations _operations;
    private PendingStopIntent _pendingStopIntent = PendingStopIntent.None;
    private bool _startInProgress;

    public RunCommandService(MainWindowViewModel viewModel, IRunCommandOperations operations)
    {
        _viewModel = viewModel;
        _operations = operations;
    }

    private IRunCommandState CurrentState => _viewModel.RunState switch
    {
        RunUiState.Stopped => StoppedState.Instance,
        RunUiState.Starting => StartingState.Instance,
        RunUiState.Running => RunningState.Instance,
        RunUiState.Paused => PausedState.Instance,
        RunUiState.Stopping => StoppingState.Instance,
        RunUiState.StopFailed => StopFailedState.Instance,
        _ => StoppedState.Instance,
    };

    public Task StartAsync()
    {
        return CurrentState.StartAsync(this);
    }

    public void TogglePause()
    {
        CurrentState.TogglePause(this);
    }

    public void StopRunWithoutReset()
    {
        CurrentState.StopRunWithoutReset(this);
    }

    public void Reset()
    {
        CurrentState.Reset(this);
    }

    private async Task StartFromStoppedAsync()
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        _startInProgress = true;
        SetStarting();
        _viewModel.StatusText = "Starting";
        _viewModel.IsAwaitingBeatSync = true;
        bool started = false;

        try
        {
            RunCommandMode mode = _operations.CurrentMode;
            started = await StartModeAsync(mode);
        }
        catch (Exception ex)
        {
            _operations.CleanupFailedStart();
            _viewModel.StatusText = "Failed to start";
            await _operations.ShowStartFailureAsync(ex);
        }
        finally
        {
            _startInProgress = false;
            if (!started && !_operations.IsClosing)
            {
                SetStopped();
                _viewModel.IsAwaitingBeatSync = false;
                if (_viewModel.StatusText == "Starting")
                {
                    _viewModel.StatusText = "Stopped";
                }
            }
        }
    }

    private void PauseRunning()
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        if (!_operations.HasActiveWorker)
        {
            return;
        }

        _operations.SetWorkersPaused(true);
        _viewModel.SetPaused();
        _viewModel.StatusText = "Paused";
    }

    private void ResumePaused()
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        _operations.SetWorkersPaused(false);
        SetRunning();
        _viewModel.StatusText = "Running";
    }

    private void StopOnly()
    {
        BeginStop(PendingStopIntent.StopOnly);
    }

    private void ResetFromPaused()
    {
        BeginStop(PendingStopIntent.ResetAfterStop);
    }

    private void ResetStopped()
    {
        CompleteReset();
    }

    private void RetryPendingStop()
    {
        PendingStopIntent intent = _pendingStopIntent == PendingStopIntent.None
            ? PendingStopIntent.StopOnly
            : _pendingStopIntent;
        BeginStop(intent);
    }

    private void BeginStop(PendingStopIntent intent)
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        _pendingStopIntent = intent;
        _operations.SetWorkersPaused(false);
        SetStopping();
        _viewModel.StatusText = intent == PendingStopIntent.ResetAfterStop
            ? "Stopping for reset"
            : "Stopping";

        RunCommandMode mode = _operations.CurrentMode;
        RunCommandStopOutcome outcome = Combine(RunCommandStopOutcome.Stopped, StopMode(mode));
        bool audioClosed = outcome == RunCommandStopOutcome.Stopped && _operations.CloseAudio();
        if (outcome != RunCommandStopOutcome.Stopped || !audioClosed)
        {
            SetStopFailed();
            if (_viewModel.StatusText is "Stopping" or "Stopping for reset")
            {
                _viewModel.StatusText = "Stop failed - press Reset to retry";
            }
            return;
        }

        CompleteStop(mode, intent);
    }

    private Task<bool> StartModeAsync(RunCommandMode mode)
    {
        return mode switch
        {
            RunCommandMode.Live => StartLiveModeAsync(),
            RunCommandMode.Playback => _operations.StartPlaybackAsync(),
            RunCommandMode.Simulation => _operations.StartSimulationAsync(),
            _ => Task.FromResult(false),
        };
    }

    private Task<bool> StartLiveModeAsync()
    {
        _operations.ConfigureLiveAudio();
        return _operations.StartLiveAsync();
    }

    private RunCommandStopOutcome StopMode(RunCommandMode mode)
    {
        return mode switch
        {
            RunCommandMode.Live => _operations.StopLive(),
            RunCommandMode.Playback => _operations.StopPlayback(),
            RunCommandMode.Simulation => _operations.StopSimulation(),
            _ => RunCommandStopOutcome.Stopped,
        };
    }

    private static bool ShouldRestoreAudioState(RunCommandMode mode)
    {
        return mode is RunCommandMode.Playback or RunCommandMode.Simulation;
    }

    private void SetStarting()
    {
        _viewModel.SetStarting();
    }

    private void SetRunning()
    {
        _viewModel.SetRunning();
    }

    private void SetStopping()
    {
        _viewModel.SetStopping();
    }

    private void SetStopFailed()
    {
        _viewModel.SetStopFailed();
    }

    private void SetStopped()
    {
        RunCommandMode mode = _operations.CurrentMode;
        _viewModel.SetModeAllowsSampleRate(RunCommandModePolicies.AllowsSelectableSampleRate(mode));
        _viewModel.SetModeAllowsGain(RunCommandModePolicies.AllowsGain(mode));
        _viewModel.SetStopped();
    }

    private static RunCommandStopOutcome Combine(RunCommandStopOutcome left, RunCommandStopOutcome right)
    {
        return left == RunCommandStopOutcome.Stopping || right == RunCommandStopOutcome.Stopping
            ? RunCommandStopOutcome.Stopping
            : RunCommandStopOutcome.Stopped;
    }

    private void CompleteStop(RunCommandMode mode, PendingStopIntent intent)
    {
        _operations.InvalidateRunSession();
        if (ShouldRestoreAudioState(mode))
        {
            _operations.RestorePlaybackOrSimulationAudioState();
        }

        SetStopped();
        _viewModel.IsAwaitingBeatSync = false;
        _pendingStopIntent = PendingStopIntent.None;

        if (intent == PendingStopIntent.ResetAfterStop)
        {
            CompleteReset();
            return;
        }

        _viewModel.StatusText = "Stopped";
    }

    private void CompleteReset()
    {
        _operations.ResetRunState();
        _operations.RefreshDevices();
        _viewModel.StatusText = "Reset";
    }

    private enum PendingStopIntent
    {
        None,
        StopOnly,
        ResetAfterStop,
    }
}
