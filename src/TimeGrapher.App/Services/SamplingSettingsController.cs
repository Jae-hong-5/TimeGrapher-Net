using System.ComponentModel;
using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

/// <summary>
/// Bridges the editable sampling-parameter view-model properties (analysis block size,
/// capture buffer length) to persistence: seeds the Settings inputs from the persisted
/// values on construction, then on each edit snaps the input to an in-range, step-aligned
/// value (so the UI only ever shows a usable value), persists it when it is valid and
/// changed, and keeps the shared <see cref="SamplingSettings.Current"/> snapshot in sync.
/// Unlike <see cref="AcceptBandController"/> there is no live re-apply — both values are
/// read at the next run start — so this controller only normalizes, saves, and updates the
/// snapshot. The persist target is injected so the gate logic is unit-testable without
/// touching the user-config file.
/// </summary>
internal sealed class SamplingSettingsController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Action<SamplingSettings> _persist;
    private SamplingSettings _applied;
    // Guards the snap write-back below from re-entering this handler.
    private bool _suppress;

    public SamplingSettingsController(
        MainWindowViewModel viewModel,
        SamplingSettings initial,
        Action<SamplingSettings> persist)
    {
        _viewModel = viewModel;
        _persist = persist;
        _applied = initial;

        // Seed before subscribing, so restoring the saved values raises no spurious save.
        // initial comes from the store (already valid + step-aligned) or Default.
        _viewModel.AnalysisBlockSize = initial.AnalysisBlockSize;
        _viewModel.CaptureBufferMs = initial.CaptureBufferMs;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Stops reacting to view-model edits (called on window close, matching the
    /// other view-model-subscriber controllers).</summary>
    public void Detach() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        if (e.PropertyName is not (
            nameof(MainWindowViewModel.AnalysisBlockSize) or
            nameof(MainWindowViewModel.CaptureBufferMs)))
        {
            return;
        }

        // Normalize the raw input (which may be off-step or out-of-range, e.g. a typed
        // value) to a usable block size / buffer, then snap the inputs back so what the
        // user sees equals what is persisted and used.
        int block = SamplingSettings.NormalizeAnalysisBlockSize(_viewModel.AnalysisBlockSize);
        int buffer = SamplingSettings.NormalizeCaptureBufferMs(_viewModel.CaptureBufferMs);

        _suppress = true;
        _viewModel.AnalysisBlockSize = block;
        _viewModel.CaptureBufferMs = buffer;
        _suppress = false;

        var candidate = new SamplingSettings(block, buffer);
        if (!_applied.ShouldReplace(candidate))
        {
            return;
        }

        _applied = candidate;
        // Keep the shared snapshot in sync so any later seed reads the live value, not the
        // startup snapshot (single source of truth across the process).
        SamplingSettings.Current = candidate;
        _persist(candidate);
    }
}
