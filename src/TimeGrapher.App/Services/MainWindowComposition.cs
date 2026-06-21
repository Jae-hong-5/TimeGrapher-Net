namespace TimeGrapher.App.Services;

/// <summary>
/// The wired application/service object graph the <see cref="MainWindowBootstrapper"/> returns; the
/// View stores these in fields. The view-model, renderers and view adapters are created by the View
/// (they need XAML controls / the window); everything else is owned here.
/// </summary>
internal sealed record MainWindowComposition(
    MainWindowSelectionCoordinator SelectionCoordinator,
    RunSelectionResolver RunSelectionResolver,
    IUserErrorLog ErrorLog,
    RecordingSessionService RecordingSessionService,
    PlaybackFileService PlaybackFileService,
    RunCommandService RunCommandService,
    MeasurementLogController MeasurementLogController,
    RunSessionController RunSessionController,
    RunControlController RunControlController,
    AcceptBandController AcceptBandController,
    AnalysisPerformanceLogger? AnalysisPerformanceLogger);
