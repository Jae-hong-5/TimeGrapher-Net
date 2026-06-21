using System.ComponentModel;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Forwards the live run-control view-model knobs (Scope Sweep multiple, active watch
/// position, Σ averaging) to the running analysis worker — the flow the MainWindow
/// code-behind used to own. Mirrors <see cref="MainWindowSelectionCoordinator"/>: a
/// view-model subscriber paired with an operations interface (<see cref="IRunSessionControls"/>).
/// </summary>
internal sealed class RunControlController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IRunSessionControls _controls;
    private readonly IRunCommandPause _pause;

    public RunControlController(
        MainWindowViewModel viewModel,
        IRunSessionControls controls,
        IRunCommandPause pause)
    {
        _viewModel = viewModel;
        _controls = controls;
        _pause = pause;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SweepMultiple):
                _controls.SetSweepMultiple(_viewModel.SweepMultiple);
                break;
            case nameof(MainWindowViewModel.SelectedPositionIndex):
                // Forward the new position to the worker first, then auto-pause an active run
                // if the operator enabled it — the order the code-behind ran these in.
                _controls.SetActivePosition((WatchPosition)_viewModel.SelectedPositionIndex);
                if (_viewModel.ShouldPauseOnPositionChange)
                {
                    _pause.PauseIfRunning();
                }
                break;
            case nameof(MainWindowViewModel.SigmaAveraging):
                _controls.SetSigmaAveraging(_viewModel.SigmaAveraging);
                break;
        }
    }
}
