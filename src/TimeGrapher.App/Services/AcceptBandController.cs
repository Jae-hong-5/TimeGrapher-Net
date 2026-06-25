using System.ComponentModel;
using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

/// <summary>
/// Bridges the editable accept-band ("normal" range) view-model properties to the
/// render/persist side (<see cref="IAcceptBandOperations"/>): seeds the Settings inputs
/// from the persisted limits on construction, then applies each consistent band edit live
/// (no run reset, so history is kept). This is the flow the MainWindow code-behind used to
/// own; it mirrors <see cref="MainWindowSelectionCoordinator"/>.
/// </summary>
internal sealed class AcceptBandController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IAcceptBandOperations _operations;

    public AcceptBandController(MainWindowViewModel viewModel, IAcceptBandOperations operations)
    {
        _viewModel = viewModel;
        _operations = operations;

        // Seed before subscribing, so restoring the saved limits raises no spurious save.
        AcceptBandValues current = _operations.CurrentBands;
        _viewModel.RateAcceptMin = (decimal)current.RateMinSPerDay;
        _viewModel.RateAcceptMax = (decimal)current.RateMaxSPerDay;
        _viewModel.AmplitudeAcceptMin = (decimal)current.AmplitudeMinDeg;
        _viewModel.AmplitudeAcceptMax = (decimal)current.AmplitudeMaxDeg;
        _viewModel.BeatErrorAcceptMag = (decimal)current.BeatErrorMagnitudeMs;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Stops reacting to view-model edits (called on window close, matching the other
    /// view-model-subscriber controllers).</summary>
    public void Detach() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel.IsSettingsWindowResetInProgress)
        {
            return;
        }

        if (e.PropertyName is not (
            nameof(MainWindowViewModel.RateAcceptMin) or
            nameof(MainWindowViewModel.RateAcceptMax) or
            nameof(MainWindowViewModel.AmplitudeAcceptMin) or
            nameof(MainWindowViewModel.AmplitudeAcceptMax) or
            nameof(MainWindowViewModel.BeatErrorAcceptMag)))
        {
            return;
        }

        _operations.TryApplyEditedBands(new AcceptBandValues(
            RateMinSPerDay: (double)_viewModel.RateAcceptMin,
            RateMaxSPerDay: (double)_viewModel.RateAcceptMax,
            AmplitudeMinDeg: (double)_viewModel.AmplitudeAcceptMin,
            AmplitudeMaxDeg: (double)_viewModel.AmplitudeAcceptMax,
            BeatErrorMagnitudeMs: (double)_viewModel.BeatErrorAcceptMag));
    }
}
