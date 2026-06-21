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
        Func<string, IMeasurementResultSink>? measurementSinkFactory = null)
    {
        var errorLog = new UserErrorLog(UserErrorLog.DefaultPath());

        var selectionCoordinator = new MainWindowSelectionCoordinator(
            viewModel, adapters.SelectionOperations, adapters.SelectionOptions);

        var runSelectionResolver = new RunSelectionResolver(
            viewModel, adapters.AveragingPeriods, BphCatalog.ManualAutoBph, BphCatalog.ManualBph);

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

        // Seed the enable state from the CLI before the controller (and the View's DataContext)
        // read it. The sink factory defaults to the real CSV logger; tests inject a fake.
        viewModel.IsMeasurementLogEnabled = startupOptions.MeasurementLogPath != null;
        measurementSinkFactory ??= static path => new MeasurementResultLogger(path);
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
            analysisFramePresenter,
            analysisPerformanceLogger);
    }
}
