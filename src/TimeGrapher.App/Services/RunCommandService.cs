using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed class RunCommandService
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IRunCommandOperations _operations;
    private bool _startInProgress;

    public RunCommandService(MainWindowViewModel viewModel, IRunCommandOperations operations)
    {
        _viewModel = viewModel;
        _operations = operations;
    }

    public async Task StartAsync()
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        _startInProgress = true;
        SetStarting();
        _viewModel.StatusText = "Starting";
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
                if (_viewModel.StatusText == "Starting")
                {
                    _viewModel.StatusText = "Stopped";
                }
            }
        }
    }

    public void TogglePause()
    {
        if (_startInProgress || _operations.IsClosing)
        {
            return;
        }

        if (_viewModel.RunState == RunUiState.Paused)
        {
            _operations.SetWorkersPaused(false);
            SetRunning();
            _viewModel.StatusText = "Running";
            return;
        }

        if (_viewModel.RunState != RunUiState.Running || !_operations.HasActiveWorker)
        {
            return;
        }

        _operations.SetWorkersPaused(true);
        _viewModel.SetPaused();
        _viewModel.StatusText = "Paused";
    }

    public void Stop()
    {
        if (_startInProgress || _operations.IsClosing ||
            _viewModel.RunState is RunUiState.Stopped or RunUiState.Stopping)
        {
            return;
        }

        _operations.SetWorkersPaused(false);
        SetStopping();
        RunCommandStopOutcome outcome = RunCommandStopOutcome.Stopped;
        RunCommandMode mode = _operations.CurrentMode;

        outcome = Combine(outcome, StopMode(mode));

        bool audioClosed = outcome == RunCommandStopOutcome.Stopped && _operations.CloseAudio();
        if (outcome != RunCommandStopOutcome.Stopped || !audioClosed)
        {
            SetStopping();
            if (_viewModel.StatusText == "Running" || _viewModel.StatusText == "Starting")
            {
                _viewModel.StatusText = "Stopping";
            }
            return;
        }

        _operations.InvalidateRunSession();
        if (ShouldRestoreAudioState(mode))
        {
            _operations.RestorePlaybackOrSimulationAudioState();
        }

        SetStopped();
        _viewModel.StatusText = "Stopped";
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
}
