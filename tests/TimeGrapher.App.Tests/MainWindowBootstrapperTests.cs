using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TimeGrapher.App;
using TimeGrapher.App.Rendering;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// MainWindowBootstrapper is the composition root: it builds the application/service graph the
/// MainWindow ctor used to construct inline. These tests confirm it produces a complete composition,
/// attaches the run-command runner, seeds the measurement-log enable flag from startup options, and
/// seeds the accept-band inputs — using fake view adapters so no window/renderer is needed.
/// </summary>
public sealed class MainWindowBootstrapperTests : IDisposable
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

    private sealed class FakeSelectionOperations : IMainWindowSelectionOperations
    {
        public IReadOnlyList<int> InputDeviceNumbers => Array.Empty<int>();
        public int AvailableSampleRateCount => 0;
        public int GetAvailableSampleRate(int index) => 0;
        public void PopulateSampleRates(int deviceNumber) { }
        public void SetCurrentSampleRate(int sampleRate) { }
        public void SetAudioInputVolume(float normalizedVolume) { }
        public void SetLiveSimulationParameters(double rateErrorSPerDay, double beatErrorMs, double watchAmplitudeDegrees, double aClusterLevelScale, double bClusterLevelScale, double cClusterLevelScale) { }
    }

    private sealed class FakeRunCommandOperations : IRunCommandOperations
    {
        public bool ResetRunStateCalled { get; private set; }

        public bool IsClosing => false;
        public bool HasActiveWorker => false;
        public RunCommandMode CurrentMode => RunCommandMode.Live;
        public void ConfigureLiveAudio() { }
        public Task<bool> StartLiveAsync() => Task.FromResult(false);
        public Task<bool> StartPlaybackAsync() => Task.FromResult(false);
        public Task<bool> StartSimulationAsync() => Task.FromResult(false);
        public void SetWorkersPaused(bool paused) { }
        public void CleanupFailedStart() { }
        public Task ShowStartFailureAsync(Exception exception) => Task.CompletedTask;
        public RunCommandStopOutcome StopLive() => RunCommandStopOutcome.Stopped;
        public RunCommandStopOutcome StopPlayback() => RunCommandStopOutcome.Stopped;
        public RunCommandStopOutcome StopSimulation() => RunCommandStopOutcome.Stopped;
        public bool CloseAudio() => true;
        public void InvalidateRunSession() { }
        public void RestorePlaybackOrSimulationAudioState() { }
        public void ResetRunState() => ResetRunStateCalled = true;
        public void RefreshDevices() { }
    }

    private sealed class FakeDialogs : ITimeGrapherDialogService
    {
        public Task<RecordSessionChoice> AskRecordSessionAsync() => Task.FromResult(RecordSessionChoice.Cancel);
        public Task<string?> PickOpenWavAsync(string currentDirectory) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenMeasurementLogAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSaveWavAsync() => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task<AiExplanationDialogResult?> AskAiExplanationAsync(AiExplanationDialogRequest request) => Task.FromResult<AiExplanationDialogResult?>(null);
        public Task<IAiExplanationDisplaySession> ShowAiExplanationProgressAsync(AiExplanationProgressDisplay display) =>
            Task.FromResult<IAiExplanationDisplaySession>(new FakeAiExplanationDisplaySession());
    }

    private sealed class FakeAiExplanationDisplaySession : IAiExplanationDisplaySession
    {
        public Task ShowStatusAsync(string statusText) => Task.CompletedTask;
        public Task ShowResultAsync(AiExplanationDisplay display) => Task.CompletedTask;
        public Task ShowFailureAsync(AiExplanationFailureDisplay failure) => Task.CompletedTask;
    }

    private sealed class FakeAcceptBandOperations : IAcceptBandOperations
    {
        public AcceptBandValues CurrentBands { get; set; } =
            new(RateMinSPerDay: -3.0, RateMaxSPerDay: 7.0, AmplitudeMinDeg: 250.0, AmplitudeMaxDeg: 310.0, BeatErrorMagnitudeMs: 0.9);
        public bool TryApplyEditedBands(AcceptBandValues candidate) => false;
        public void ApplyCurrentBands() { }
    }

    private sealed class FakeMeasurementSink : IMeasurementResultSink
    {
        public void ObserveDisplayed(AnalysisFrame frame) { }
        public void Dispose() { }
    }

    private static MainWindowViewAdapters Adapters(FakeRunCommandOperations runCommandOps, FakeAcceptBandOperations acceptBandOps) =>
        new(
            new FakeSelectionOperations(),
            runCommandOps,
            new FakeDialogs(),
            acceptBandOps,
            new MainWindowSelectionOptions("Playback", "Simulation"));

    private static MainWindowRunSessionCallbacks Callbacks() =>
        new(
            // Never invoked by Build (only stored on the RunSessionController), so a null config is fine.
            _ => default(AnalysisWorker.Config)!,
            () => { },
            () => { },
            () => { },
            _ => { });

    [Fact]
    public void Build_ProducesCompleteComposition()
    {
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();

        MainWindowComposition composition = MainWindowBootstrapper.Build(
            vm, Adapters(new FakeRunCommandOperations(), new FakeAcceptBandOperations()), Callbacks(),
            AppStartupOptions.Parse(Array.Empty<string>()));

        Assert.NotNull(composition.SelectionCoordinator);
        Assert.NotNull(composition.RunSelectionResolver);
        Assert.NotNull(composition.ErrorLog);
        Assert.NotNull(composition.RecordingSessionService);
        Assert.NotNull(composition.PlaybackFileService);
        Assert.NotNull(composition.RunCommandService);
        Assert.NotNull(composition.MeasurementLogController);
        Assert.NotNull(composition.RunSessionController);
        Assert.NotNull(composition.RunControlController);
        Assert.NotNull(composition.AcceptBandController);
        Assert.NotNull(composition.SamplingSettingsController);
        Assert.Null(composition.AnalysisPerformanceLogger); // no --analysis-log
    }

    [Fact]
    public void Build_AttachesRunCommandRunner()
    {
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();
        var runCommandOps = new FakeRunCommandOperations();

        _ = MainWindowBootstrapper.Build(
            vm, Adapters(runCommandOps, new FakeAcceptBandOperations()), Callbacks(),
            AppStartupOptions.Parse(Array.Empty<string>()));

        // Reset from Stopped runs CompleteReset -> ResetRunState on the operations; this only
        // happens if the runner the command invokes was attached to the view-model.
        vm.ResetCommand.Execute(null);

        Assert.True(runCommandOps.ResetRunStateCalled);
        Assert.Equal("Reset", vm.StatusText);
    }

    [Fact]
    public void Build_SeedsMeasurementLogFromDefaultWhenNoCliPath()
    {
        // No CLI path: the toggle seeds from the persisted setting, which is on by default.
        AppSettings.Current = AppSettings.Default;
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();

        _ = MainWindowBootstrapper.Build(
            vm, Adapters(new FakeRunCommandOperations(), new FakeAcceptBandOperations()), Callbacks(),
            AppStartupOptions.Parse(Array.Empty<string>()));

        Assert.True(vm.IsMeasurementLogEnabled);
    }

    [Fact]
    public void Build_DoesNotSeedMeasurementLogToggleFromCliPath()
    {
        // The persisted toggle is off here, so any post-Build "on" could only come from
        // the CLI path — which must not happen.
        AppSettings.Current = AppSettings.Default with
        {
            SettingsWindow = SettingsWindowSettings.Default with { MeasurementLogEnabled = false },
        };
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();

        // A one-shot --measurement-log launch must NOT flip the persisted toggle: that
        // value is saved on the next settings edit and would silently keep logging on for
        // future launches. The CLI run still logs (driven by MeasurementLogController from
        // its path), but the persisted toggle stays at its saved value. A fake sink factory
        // means the logging path opens no CSV file.
        _ = MainWindowBootstrapper.Build(
            vm, Adapters(new FakeRunCommandOperations(), new FakeAcceptBandOperations()), Callbacks(),
            AppStartupOptions.Parse(new[] { "--measurement-log", "ignored.csv" }),
            (_, _) => new FakeMeasurementSink());

        Assert.False(vm.IsMeasurementLogEnabled);
    }

    [Fact]
    public void Build_SeedsPersistedLeftPanelAndSettingsWindowValues()
    {
        AppSettings.Current = AppSettings.Default with
        {
            LeftPanel = LeftPanelSettings.Default with
            {
                Gain = 420.0,
                LiftAngle = 54.0,
                SimulationErrorRate = -12.0,
                SimulationAmplitude = 280.0,
                SimulationBeatError = 0.5,
                SimulationRealistic = false,
            },
            SettingsWindow = SettingsWindowSettings.Default with
            {
                UseCOnset = true,
                WeakAOnsetRescue = true,
                SpuriousBeatRejection = false,
                PauseOnPositionChange = true,
                HighPassCutoffText = "180",
                MeasurementLogEnabled = true,
                VerdictMinimumBeats = 45,
            },
        };
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();

        _ = MainWindowBootstrapper.Build(
            vm, Adapters(new FakeRunCommandOperations(), new FakeAcceptBandOperations()), Callbacks(),
            AppStartupOptions.Parse(Array.Empty<string>()));

        Assert.Equal(420.0, vm.Gain);
        Assert.Equal(54m, vm.LiftAngle);
        Assert.Equal(-12m, vm.SimErrorRate);
        Assert.Equal(280m, vm.SimAmplitude);
        Assert.Equal(0.5m, vm.SimBeatError);
        Assert.False(vm.Realistic);
        Assert.True(vm.UseCOnset);
        Assert.True(vm.WeakAOnsetRescue);
        Assert.False(vm.SpuriousBeatRejection);
        Assert.True(vm.PauseOnPositionChange);
        Assert.Equal("180", vm.HighPassCutoffText);
        Assert.True(vm.IsMeasurementLogEnabled);
        Assert.Equal(45m, vm.VerdictMinimumBeats);
    }

    [Fact]
    public void Build_SeedsAcceptBandInputsFromOperations()
    {
        MainWindowViewModel vm = MainWindowBootstrapper.CreateViewModel();
        var acceptBandOps = new FakeAcceptBandOperations();

        _ = MainWindowBootstrapper.Build(
            vm, Adapters(new FakeRunCommandOperations(), acceptBandOps), Callbacks(),
            AppStartupOptions.Parse(Array.Empty<string>()));

        Assert.Equal(-3.0m, vm.RateAcceptMin);
        Assert.Equal(0.9m, vm.BeatErrorAcceptMag);
    }
}
