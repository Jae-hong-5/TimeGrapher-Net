namespace TimeGrapher.App.Services;

/// <summary>
/// The View-bound inputs the <see cref="MainWindowBootstrapper"/> wires the service graph around:
/// the operations adapters (which need the MainWindow), the accept-band operations (which need the
/// graph renderer), the dialog service, and the selection options.
/// These stay View-created because they depend on XAML controls / the window; grouping them keeps
/// <see cref="MainWindowBootstrapper.Build"/>'s signature small.
/// </summary>
internal sealed record MainWindowViewAdapters(
    IMainWindowSelectionOperations SelectionOperations,
    IRunCommandOperations RunCommandOperations,
    ITimeGrapherDialogService Dialogs,
    IAcceptBandOperations AcceptBandOperations,
    MainWindowSelectionOptions SelectionOptions);
