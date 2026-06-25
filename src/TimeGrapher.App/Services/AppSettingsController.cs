using System.ComponentModel;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal readonly record struct AppSettingsSelection(
    string? InputDeviceName,
    int SampleRate,
    int Bph,
    int SimulationBph);

internal sealed class AppSettingsController : ISettingsWindowResetRunner
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<AppSettingsSelection> _selection;
    private readonly Action<AppSettings> _persist;

    public AppSettingsController(
        MainWindowViewModel viewModel,
        Func<AppSettingsSelection> selection,
        Action<AppSettings> persist)
    {
        _viewModel = viewModel;
        _selection = selection;
        _persist = persist;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public static void SeedViewModel(MainWindowViewModel viewModel, AppSettings settings, bool measurementLogEnabled)
    {
        LeftPanelSettings left = settings.LeftPanel;
        SettingsWindowSettings window = settings.SettingsWindow;
        viewModel.Gain = left.Gain;
        viewModel.LiftAngle = (decimal)left.LiftAngle;
        viewModel.SimErrorRate = (decimal)left.SimulationErrorRate;
        viewModel.SimAmplitude = (decimal)left.SimulationAmplitude;
        viewModel.SimBeatError = (decimal)left.SimulationBeatError;
        viewModel.Realistic = left.SimulationRealistic;
        viewModel.UseCOnset = window.UseCOnset;
        viewModel.WeakAOnsetRescue = window.WeakAOnsetRescue;
        viewModel.PauseOnPositionChange = window.PauseOnPositionChange;
        viewModel.HighPassCutoffText = window.HighPassCutoffText;
        viewModel.IsMeasurementLogEnabled = measurementLogEnabled;
    }

    public void ResetSettingsWindow() => ResetSettingsWindow(_viewModel);

    private static void ResetSettingsWindow(MainWindowViewModel viewModel)
    {
        SamplingSettings sampling = SamplingSettings.Default;
        SettingsWindowSettings window = SettingsWindowSettings.Default;
        AcceptBandSettings bands = AcceptBandSettings.Default;
        viewModel.UseCOnset = window.UseCOnset;
        viewModel.WeakAOnsetRescue = window.WeakAOnsetRescue;
        viewModel.PauseOnPositionChange = window.PauseOnPositionChange;
        viewModel.AveragingPeriod = sampling.AveragingPeriod;
        viewModel.AnalysisBlockSize = sampling.AnalysisBlockSize;
        viewModel.CaptureBufferMs = sampling.CaptureBufferMs;
        viewModel.HighPassCutoffText = window.HighPassCutoffText;
        ResetAcceptBands(viewModel, bands);
        viewModel.IsMeasurementLogEnabled = window.MeasurementLogEnabled;
    }

    public void Detach() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(MainWindowViewModel.SelectedInputDeviceIndex) or
            nameof(MainWindowViewModel.SelectedSampleRateIndex) or
            nameof(MainWindowViewModel.SelectedBphIndex) or
            nameof(MainWindowViewModel.Gain) or
            nameof(MainWindowViewModel.LiftAngle) or
            nameof(MainWindowViewModel.SelectedSimBphIndex) or
            nameof(MainWindowViewModel.SimErrorRate) or
            nameof(MainWindowViewModel.SimAmplitude) or
            nameof(MainWindowViewModel.SimBeatError) or
            nameof(MainWindowViewModel.Realistic) or
            nameof(MainWindowViewModel.UseCOnset) or
            nameof(MainWindowViewModel.WeakAOnsetRescue) or
            nameof(MainWindowViewModel.PauseOnPositionChange) or
            nameof(MainWindowViewModel.HighPassCutoffText) or
            nameof(MainWindowViewModel.IsMeasurementLogEnabled)))
        {
            return;
        }

        AppSettingsSelection selection = _selection();
        var next = AppSettings.Current with
        {
            Sampling = SamplingSettings.Current,
            AcceptBands = AcceptBandSettings.Current,
            LeftPanel = new LeftPanelSettings(
                selection.InputDeviceName,
                selection.SampleRate,
                _viewModel.Gain,
                selection.Bph,
                (double)_viewModel.LiftAngle,
                selection.SimulationBph,
                (double)_viewModel.SimErrorRate,
                (double)_viewModel.SimAmplitude,
                (double)_viewModel.SimBeatError,
                _viewModel.Realistic),
            SettingsWindow = new SettingsWindowSettings(
                _viewModel.UseCOnset,
                _viewModel.WeakAOnsetRescue,
                _viewModel.PauseOnPositionChange,
                _viewModel.HighPassCutoffText,
                _viewModel.IsMeasurementLogEnabled),
        };

        if (!next.IsValid || next == AppSettings.Current)
        {
            return;
        }

        AppSettings.Current = next;
        _persist(next);
    }

    private static void ResetAcceptBands(MainWindowViewModel viewModel, AcceptBandSettings defaults)
    {
        viewModel.RateAcceptMin = (decimal)-AcceptBandSettings.RateLimitSPerDay;
        viewModel.RateAcceptMax = (decimal)AcceptBandSettings.RateLimitSPerDay;
        viewModel.AmplitudeAcceptMin = (decimal)AcceptBandSettings.AmplitudeFloorDeg;
        viewModel.AmplitudeAcceptMax = (decimal)AcceptBandSettings.AmplitudeCeilingDeg;
        viewModel.RateAcceptMin = (decimal)defaults.RateMinSPerDay;
        viewModel.RateAcceptMax = (decimal)defaults.RateMaxSPerDay;
        viewModel.AmplitudeAcceptMin = (decimal)defaults.AmplitudeMinDeg;
        viewModel.AmplitudeAcceptMax = (decimal)defaults.AmplitudeMaxDeg;
        viewModel.BeatErrorAcceptMag = (decimal)defaults.BeatErrorMagnitudeMs;
    }
}
