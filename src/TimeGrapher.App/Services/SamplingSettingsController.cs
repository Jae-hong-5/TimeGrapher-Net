using System.ComponentModel;
using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

/// <summary>
/// Bridges the editable sampling-parameter view-model properties (analysis block size,
/// capture buffer length) to persistence: seeds the Settings inputs from the persisted
/// values on construction, then persists each valid, changed edit. Unlike
/// <see cref="AcceptBandController"/> there is no live re-apply — both values are read at
/// the next run start — so this controller only validates and saves. The persist target
/// is injected so the gate logic is unit-testable without touching the user-config file.
/// </summary>
internal sealed class SamplingSettingsController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action<SamplingSettings> _persist;
    private SamplingSettings _applied;

    public SamplingSettingsController(
        MainWindowViewModel viewModel,
        SamplingSettings initial,
        Action<SamplingSettings> persist)
    {
        _viewModel = viewModel;
        _persist = persist;
        _applied = initial;

        // Seed before subscribing, so restoring the saved values raises no spurious save.
        _viewModel.AnalysisBlockSize = initial.AnalysisBlockSize;
        _viewModel.CaptureBufferMs = initial.CaptureBufferMs;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Stops reacting to view-model edits (called on window close, matching the
    /// other view-model-subscriber controllers).</summary>
    public void Detach() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(MainWindowViewModel.AnalysisBlockSize) or
            nameof(MainWindowViewModel.CaptureBufferMs)))
        {
            return;
        }

        var candidate = new SamplingSettings(_viewModel.AnalysisBlockSize, _viewModel.CaptureBufferMs);
        if (!_applied.ShouldReplace(candidate))
        {
            return;
        }

        _applied = candidate;
        _persist(candidate);
    }
}
