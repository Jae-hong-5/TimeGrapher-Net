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
    private readonly IAcceptBandOperations? _acceptBandOperations;
    private readonly Action<SamplingSettings>? _syncSamplingSettings;

    public AppSettingsController(
        MainWindowViewModel viewModel,
        Func<AppSettingsSelection> selection,
        Action<AppSettings> persist,
        IAcceptBandOperations? acceptBandOperations = null,
        Action<SamplingSettings>? syncSamplingSettings = null)
    {
        _viewModel = viewModel;
        _selection = selection;
        _persist = persist;
        _acceptBandOperations = acceptBandOperations;
        _syncSamplingSettings = syncSamplingSettings;
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
        viewModel.SpuriousBeatRejection = window.SpuriousBeatRejection;
        viewModel.PauseOnPositionChange = window.PauseOnPositionChange;
        viewModel.HighPassCutoffText = window.HighPassCutoffText;
        viewModel.IsMeasurementLogEnabled = measurementLogEnabled;
    }

    public void ResetSettingsWindow()
    {
        SamplingSettings sampling = SamplingSettings.Default;
        SettingsWindowSettings window = SettingsWindowSettings.Default;
        AcceptBandSettings bands = AcceptBandSettings.Default;

        _viewModel.RunSettingsWindowReset(() =>
        {
            _viewModel.UseCOnset = window.UseCOnset;
            _viewModel.WeakAOnsetRescue = window.WeakAOnsetRescue;
            _viewModel.SpuriousBeatRejection = window.SpuriousBeatRejection;
            _viewModel.PauseOnPositionChange = window.PauseOnPositionChange;
            _viewModel.AveragingPeriod = sampling.AveragingPeriod;
            _viewModel.AnalysisBlockSize = sampling.AnalysisBlockSize;
            _viewModel.CaptureBufferMs = sampling.CaptureBufferMs;
            _viewModel.HighPassCutoffText = window.HighPassCutoffText;
            ResetAcceptBands(_viewModel, bands);
            _viewModel.IsMeasurementLogEnabled = window.MeasurementLogEnabled;
        });

        SamplingSettings.Current = sampling;
        _syncSamplingSettings?.Invoke(sampling);
        AcceptBandSettings.Current = bands;
        Persist(BuildSnapshot(sampling, bands, window));
        _acceptBandOperations?.ApplyCurrentBands();
    }

    public void Detach()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel.IsSettingsWindowResetInProgress)
        {
            return;
        }

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
            nameof(MainWindowViewModel.SpuriousBeatRejection) or
            nameof(MainWindowViewModel.PauseOnPositionChange) or
            nameof(MainWindowViewModel.HighPassCutoffText) or
            nameof(MainWindowViewModel.IsMeasurementLogEnabled)))
        {
            return;
        }

        AppSettings next = BuildSnapshot(
            SamplingSettings.Current,
            AcceptBandSettings.Current,
            new SettingsWindowSettings(
                _viewModel.UseCOnset,
                _viewModel.WeakAOnsetRescue,
                _viewModel.SpuriousBeatRejection,
                _viewModel.PauseOnPositionChange,
                _viewModel.HighPassCutoffText,
                _viewModel.IsMeasurementLogEnabled));

        if (!next.IsValid || next == AppSettings.Current)
        {
            return;
        }

        AppSettings.Current = next;
        _persist(next);
    }

    private void Persist(AppSettings settings)
    {
        if (!settings.IsValid)
        {
            return;
        }

        AppSettings.Current = settings;
        _persist(settings);
    }

    private AppSettings BuildSnapshot(
        SamplingSettings sampling,
        AcceptBandSettings acceptBands,
        SettingsWindowSettings settingsWindow)
    {
        AppSettingsSelection selection = _selection();
        return AppSettings.Current with
        {
            Sampling = sampling,
            AcceptBands = acceptBands,
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
            SettingsWindow = settingsWindow,
        };
    }

    private static void ResetAcceptBands(MainWindowViewModel viewModel, AcceptBandSettings defaults)
    {
        viewModel.RateAcceptMin = (decimal)defaults.RateMinSPerDay;
        viewModel.RateAcceptMax = (decimal)defaults.RateMaxSPerDay;
        viewModel.AmplitudeAcceptMin = (decimal)defaults.AmplitudeMinDeg;
        viewModel.AmplitudeAcceptMax = (decimal)defaults.AmplitudeMaxDeg;
        viewModel.BeatErrorAcceptMag = (decimal)defaults.BeatErrorMagnitudeMs;
    }
}
