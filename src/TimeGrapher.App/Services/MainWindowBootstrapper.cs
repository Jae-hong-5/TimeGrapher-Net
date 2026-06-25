using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Detection;

namespace TimeGrapher.App.Services;

/// <summary>
/// Composition root for MainWindow's application/service graph. Invoked once after
/// InitializeComponent, so the View ctor no longer constructs the service graph itself: the View
/// creates the view-model, the renderers (XAML controls) and the view adapters, then asks
/// <see cref="Build"/> to wire the rest. No DI container — this is plain construction kept in one
/// place, with the View-bound inputs passed in.
/// </summary>
internal static class MainWindowBootstrapper
{
    public static MainWindowViewModel CreateViewModel() => new();

    public static MainWindowComposition Build(
        MainWindowViewModel viewModel,
        MainWindowViewAdapters adapters,
        MainWindowRunSessionCallbacks runSessionCallbacks,
        AppStartupOptions startupOptions,
        Func<string, decimal, IMeasurementResultSink>? measurementSinkFactory = null)
    {
        var errorLog = new UserErrorLog(UserErrorLog.DefaultPath());

        var selectionCoordinator = new MainWindowSelectionCoordinator(
            viewModel, adapters.SelectionOperations, adapters.SelectionOptions);

        var runSelectionResolver = new RunSelectionResolver(
            viewModel, BphCatalog.ManualAutoBph, BphCatalog.ManualBph);

        var recordingSessionService = new RecordingSessionService(
            adapters.Dialogs, new QueuedRecordingWriterFactory(), errorLog);

        var playbackFileService = new PlaybackFileService(adapters.Dialogs, errorLog);

        var runCommandService = new RunCommandService(viewModel, adapters.RunCommandOperations);
        // The play/pause and reset commands invoke the service directly; attach immediately after
        // construction, before any load/initialize path can execute a command.
        viewModel.AttachRunCommandRunner(runCommandService);

        AnalysisPerformanceLogger? analysisPerformanceLogger =
            startupOptions.AnalysisLogPath is string analysisLogPath
                ? new AnalysisPerformanceLogger(LogFilePaths.EnsureParentDirectory(analysisLogPath))
                : null;

        // Seed the persisted toggle from the saved value ONLY. A one-shot
        // --measurement-log launch must not flip the persisted toggle true (it would
        // then be saved on the next unrelated settings edit and silently keep logging
        // on for future launches); the CLI run still logs because MeasurementLogController
        // drives it from the supplied path independently of this toggle.
        AppSettingsController.SeedViewModel(
            viewModel,
            AppSettings.Current,
            AppSettings.Current.SettingsWindow.MeasurementLogEnabled);
        measurementSinkFactory ??= static (path, liftAngleDeg) => new MeasurementResultLogger(path, liftAngleDeg);
        var measurementLogController = new MeasurementLogController(
            viewModel, startupOptions.MeasurementLogPath, measurementSinkFactory);

        var runSessionController = new RunSessionController(
            runSessionCallbacks.CreateAnalysisConfig,
            runSessionCallbacks.ResetBeforeRun,
            runSessionCallbacks.ClearPendingFrames,
            runSessionCallbacks.ResetRenderTiming,
            runSessionCallbacks.OnAnalysisFrameReady,
            status => viewModel.StatusText = status,
            errorLog);

        var acceptBandController = new AcceptBandController(viewModel, adapters.AcceptBandOperations);
        // Seeds the Settings inputs from the persisted run-start parameters and saves each
        // valid edit; the values are read at the next run start, not applied live.
        var samplingSettingsController = new SamplingSettingsController(
            viewModel, SamplingSettings.Current, AppSettingsStore.SaveSampling);
        var runControlController = new RunControlController(viewModel, runSessionController, runCommandService);
        var analysisFramePresenter = new AnalysisFramePresenter(viewModel, errorLog);

        return new MainWindowComposition(
            selectionCoordinator,
            runSelectionResolver,
            errorLog,
            recordingSessionService,
            playbackFileService,
            runCommandService,
            measurementLogController,
            runSessionController,
            runControlController,
            acceptBandController,
            samplingSettingsController,
            analysisFramePresenter,
            analysisPerformanceLogger);
    }
}
