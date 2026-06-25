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

    public void Dispose() => AppSettings.Current = _savedAppSettings;

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
    public void ResetSettingsWindow_RestoresOnlySettingsWindowControls()
    {
        var viewModel = new MainWindowViewModel
        {
            Gain = 777.0,
            LiftAngle = 60m,
            SimErrorRate = -30m,
            SimAmplitude = 250m,
            SimBeatError = 2m,
            Realistic = false,
            UseCOnset = true,
            WeakAOnsetRescue = true,
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
            IsMeasurementLogEnabled = true,
        };

        var controller = new AppSettingsController(
            viewModel,
            () => new AppSettingsSelection(null, 48000, 0, 28800),
            _ => { });

        controller.ResetSettingsWindow();

        Assert.Equal(777.0, viewModel.Gain);
        Assert.Equal(60m, viewModel.LiftAngle);
        Assert.Equal(-30m, viewModel.SimErrorRate);
        Assert.Equal(250m, viewModel.SimAmplitude);
        Assert.Equal(2m, viewModel.SimBeatError);
        Assert.False(viewModel.Realistic);
        Assert.False(viewModel.UseCOnset);
        Assert.False(viewModel.WeakAOnsetRescue);
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
        Assert.False(viewModel.IsMeasurementLogEnabled);
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
