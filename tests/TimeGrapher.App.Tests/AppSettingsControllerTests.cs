using System;
using System.Collections.Generic;
using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AppSettingsControllerTests : IDisposable
{
    private readonly AppSettings _savedAppSettings = AppSettings.Current;
    private readonly SamplingSettings _savedSamplingSettings = SamplingSettings.Current;
    private readonly AcceptBandSettings _savedAcceptBandSettings = AcceptBandSettings.Current;

    public void Dispose()
    {
        AppSettings.Current = _savedAppSettings;
        SamplingSettings.Current = _savedSamplingSettings;
        AcceptBandSettings.Current = _savedAcceptBandSettings;
    }

    private sealed class FakeAcceptBandOperations : IAcceptBandOperations
    {
        public AcceptBandValues CurrentBands { get; set; }
        public List<AcceptBandValues> Applied { get; } = new();
        public int ApplyCurrentBandsCalls { get; private set; }

        public bool TryApplyEditedBands(AcceptBandValues candidate)
        {
            Applied.Add(candidate);
            return true;
        }

        public void ApplyCurrentBands() => ApplyCurrentBandsCalls++;
    }

    [Fact]
    public void LeftPanelEdit_PersistsSnapshotWithResolvedSelectionValues()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default;
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection("Live: Mic", 96000, 21600, 18000),
            persisted.Add);

        viewModel.Gain = 420.0;

        AppSettings saved = Assert.Single(persisted);
        Assert.Equal("Live: Mic", saved.LeftPanel.InputDeviceName);
        Assert.Equal(96000, saved.LeftPanel.SampleRate);
        Assert.Equal(21600, saved.LeftPanel.Bph);
        Assert.Equal(18000, saved.LeftPanel.SimulationBph);
        Assert.Equal(420.0, saved.LeftPanel.Gain);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void SignalScaleEdit_PersistsClusterScaleSnapshot()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default;
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        viewModel.SimSignalAScale = 0.4m;

        AppSettings saved = Assert.Single(persisted);
        Assert.Equal(0.4, saved.LeftPanel.SimulationSignalAScale);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void SeedViewModel_AppliesPersistedClusterScales()
    {
        var viewModel = new MainWindowViewModel();
        AppSettings settings = AppSettings.Default with
        {
            LeftPanel = AppSettings.Default.LeftPanel with
            {
                SimulationSignalAScale = 0.2,
                SimulationSignalBScale = 1.5,
                SimulationSignalCScale = 0.8,
            },
        };

        AppSettingsController.SeedViewModel(viewModel, settings, measurementLogEnabled: false);

        Assert.Equal(0.2m, viewModel.SimSignalAScale);
        Assert.Equal(1.5m, viewModel.SimSignalBScale);
        Assert.Equal(0.8m, viewModel.SimSignalCScale);
    }

    [Fact]
    public void SeedViewModel_AppliesPersistedRescueStrengthStep()
    {
        var viewModel = new MainWindowViewModel();
        AppSettings settings = AppSettings.Default with
        {
            SettingsWindow = AppSettings.Default.SettingsWindow with
            {
                WeakAOnsetRescueStrengthStep = 8,
                VerdictMinimumBeats = 45,
            },
        };

        AppSettingsController.SeedViewModel(viewModel, settings, measurementLogEnabled: false);

        Assert.Equal(8, viewModel.WeakAOnsetRescueStrengthStep);
        Assert.Equal(45m, viewModel.VerdictMinimumBeats);
    }

    [Fact]
    public void SettingsWindowEdit_PersistsSettingsWindowSnapshot()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default;
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        viewModel.UseCOnset = true;

        AppSettings saved = Assert.Single(persisted);
        Assert.True(saved.SettingsWindow.UseCOnset);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void SettingsWindowEdit_PersistsSpuriousBeatRejection()
    {
        // F7: flipping the spurious-beat toggle must persist. Guards that the field
        // stays in the controller's watched-property list and the saved snapshot.
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default; // SpuriousBeatRejection defaults true
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        viewModel.SpuriousBeatRejection = false;

        AppSettings saved = Assert.Single(persisted);
        Assert.False(saved.SettingsWindow.SpuriousBeatRejection);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void SettingsWindowEdit_PersistsWeakARescueStrengthStep()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default;
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        viewModel.WeakAOnsetRescueStrengthStep = 8;

        AppSettings saved = Assert.Single(persisted);
        Assert.Equal(8, saved.SettingsWindow.WeakAOnsetRescueStrengthStep);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void SettingsWindowEdit_PersistsVerdictMinimumBeats()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        AppSettings.Current = AppSettings.Default;
        _ = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        viewModel.VerdictMinimumBeats = 45m;

        AppSettings saved = Assert.Single(persisted);
        Assert.Equal(45, saved.SettingsWindow.VerdictMinimumBeats);
        Assert.Equal(saved, AppSettings.Current);
    }

    [Fact]
    public void ResetSettingsWindow_RestoresSettingsAndLeftPanelControls()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedInputDeviceIndex = 2,
            SelectedSampleRateIndex = 3,
            SelectedBphIndex = 4,
            SelectedSimBphIndex = 5,
            Gain = 777.0,
            LiftAngle = 60m,
            SimErrorRate = -30m,
            SimAmplitude = 250m,
            SimBeatError = 2m,
            Realistic = false,
            SimSignalAScale = 0.4m,
            SimSignalBScale = 1.6m,
            SimSignalCScale = 0.7m,
            UseCOnset = true,
            WeakAOnsetRescue = true,
            WeakAOnsetRescueStrengthStep = 1,
            SpuriousBeatRejection = false,
            PauseOnPositionChange = true,
            AveragingPeriod = 45m,
            AnalysisBlockSize = 8192m,
            CaptureBufferMs = 50m,
            HighPassCutoffText = "180",
            RateAcceptMin = 10m,
            RateAcceptMax = 20m,
            AmplitudeAcceptMin = 10m,
            AmplitudeAcceptMax = 20m,
            BeatErrorAcceptMag = 2m,
            VerdictMinimumBeats = 45m,
            IsMeasurementLogEnabled = true,
        };
        LeftPanelSettings? resetLeftPanel = null;

        var controller = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection("Live: Mic", 96000, 21600, 18000),
            _ => { },
            resetLeftPanelSelections: defaults =>
            {
                resetLeftPanel = defaults;
                viewModel.SelectedInputDeviceIndex = 0;
                viewModel.SelectedSampleRateIndex = 1;
                viewModel.SelectedBphIndex = 0;
                viewModel.SelectedSimBphIndex = 3;
            });

        controller.ResetSettingsWindow();

        Assert.Same(LeftPanelSettings.Default, resetLeftPanel);
        Assert.Equal(0, viewModel.SelectedInputDeviceIndex);
        Assert.Equal(1, viewModel.SelectedSampleRateIndex);
        Assert.Equal(0, viewModel.SelectedBphIndex);
        Assert.Equal(3, viewModel.SelectedSimBphIndex);
        Assert.Equal(LeftPanelSettings.Default.Gain, viewModel.Gain);
        Assert.Equal((decimal)LeftPanelSettings.Default.LiftAngle, viewModel.LiftAngle);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationErrorRate, viewModel.SimErrorRate);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationAmplitude, viewModel.SimAmplitude);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationBeatError, viewModel.SimBeatError);
        Assert.Equal(LeftPanelSettings.Default.SimulationRealistic, viewModel.Realistic);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationSignalAScale, viewModel.SimSignalAScale);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationSignalBScale, viewModel.SimSignalBScale);
        Assert.Equal((decimal)LeftPanelSettings.Default.SimulationSignalCScale, viewModel.SimSignalCScale);
        Assert.False(viewModel.UseCOnset);
        Assert.True(viewModel.WeakAOnsetRescue);
        Assert.Equal(WeakAOnsetRescueStrengthPolicy.StandardStep, viewModel.WeakAOnsetRescueStrengthStep);
        Assert.True(viewModel.SpuriousBeatRejection);
        Assert.False(viewModel.PauseOnPositionChange);
        Assert.Equal(SamplingSettings.Default.AveragingPeriod, (int)viewModel.AveragingPeriod);
        Assert.Equal(SamplingSettings.Default.AnalysisBlockSize, (int)viewModel.AnalysisBlockSize);
        Assert.Equal(SamplingSettings.Default.CaptureBufferMs, (int)viewModel.CaptureBufferMs);
        Assert.Equal(SettingsWindowSettings.Default.HighPassCutoffText, viewModel.HighPassCutoffText);
        Assert.Equal((decimal)AcceptBandSettings.Default.RateMinSPerDay, viewModel.RateAcceptMin);
        Assert.Equal((decimal)AcceptBandSettings.Default.RateMaxSPerDay, viewModel.RateAcceptMax);
        Assert.Equal((decimal)AcceptBandSettings.Default.AmplitudeMinDeg, viewModel.AmplitudeAcceptMin);
        Assert.Equal((decimal)AcceptBandSettings.Default.AmplitudeMaxDeg, viewModel.AmplitudeAcceptMax);
        Assert.Equal((decimal)AcceptBandSettings.Default.BeatErrorMagnitudeMs, viewModel.BeatErrorAcceptMag);
        Assert.Equal(SettingsWindowSettings.Default.VerdictMinimumBeats, (int)viewModel.VerdictMinimumBeats);
        Assert.False(viewModel.IsMeasurementLogEnabled);
    }

    [Fact]
    public void ResetSettingsWindow_SuppressesIntermediateSideEffectsAndPersistsFinalSnapshotOnce()
    {
        var customSampling = new SamplingSettings(
            8192,
            SamplingSettings.Default.CaptureBufferMs,
            SamplingSettings.Default.AveragingPeriod);
        var customBands = new AcceptBandSettings(-8.0, 8.0, 250.0, 310.0, 1.2);
        AppSettings.Current = AppSettings.Default with
        {
            Sampling = customSampling,
            AcceptBands = customBands,
            SettingsWindow = new SettingsWindowSettings(true, false, false, true, "180", true),
        };
        SamplingSettings.Current = customSampling;
        AcceptBandSettings.Current = customBands;
        var viewModel = new MainWindowViewModel();
        AppSettingsController.SeedViewModel(viewModel, AppSettings.Current, measurementLogEnabled: true);
        var acceptOps = new FakeAcceptBandOperations
        {
            CurrentBands = new AcceptBandValues(-8.0, 8.0, 250.0, 310.0, 1.2),
        };
        _ = new AcceptBandController(viewModel, acceptOps);
        var samplingPersisted = new List<SamplingSettings>();
        var samplingController = new SamplingSettingsController(viewModel, customSampling, samplingPersisted.Add);
        var appPersisted = new List<AppSettings>();
        var controller = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection("Live: Mic", 96000, 21600, 18000),
            appPersisted.Add,
            acceptOps,
            samplingController.SyncAppliedSnapshot);

        controller.ResetSettingsWindow();

        Assert.Empty(acceptOps.Applied);
        Assert.Empty(samplingPersisted);
        Assert.Equal(1, acceptOps.ApplyCurrentBandsCalls);
        Assert.Equal(SamplingSettings.Default, SamplingSettings.Current);
        Assert.Equal(AcceptBandSettings.Default, AcceptBandSettings.Current);
        AppSettings saved = Assert.Single(appPersisted);
        Assert.Equal(SamplingSettings.Default, saved.Sampling);
        Assert.Equal(AcceptBandSettings.Default, saved.AcceptBands);
        Assert.Equal(SettingsWindowSettings.Default, saved.SettingsWindow);
        Assert.Equal(LeftPanelSettings.Default, saved.LeftPanel);

        viewModel.AnalysisBlockSize = customSampling.AnalysisBlockSize;

        Assert.Equal(customSampling, Assert.Single(samplingPersisted));
        Assert.Equal(customSampling, SamplingSettings.Current);
    }

    [Fact]
    public void AfterDetach_EditsAreNotPersisted()
    {
        var viewModel = new MainWindowViewModel();
        var persisted = new List<AppSettings>();
        var controller = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            persisted.Add);

        controller.Detach();
        viewModel.Gain = 420.0;

        Assert.Empty(persisted);
    }

}
