using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Owns the optional measurement-result CSV log: opens a fresh log at each run start (so the
/// log's lift-angle header records the angle that run actually uses), keeps writing across a
/// pause/resume, and closes the sink when a run stops, logging is disabled, or on dispose.
/// Consumes the CLI --measurement-log path for the first run, then timestamps later runs. The
/// flow the MainWindow code-behind used to own; mirrors the other view-model-subscriber controllers.
/// The sink is created through an injected factory so the controller is testable without opening a file.
/// </summary>
internal sealed class MeasurementLogController : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<string, decimal, IMeasurementResultSink> _sinkFactory;
    private string? _pendingLogPath;
    private bool _cliLogging;
    private IMeasurementResultSink? _sink;
    private RunUiState _previousRunState;

    /// <summary>
    /// Raised when a log that dropped rows is closed (argument: the dropped-row count), so
    /// the silent CSV loss is surfaced instead of leaving an incomplete file looking complete.
    /// </summary>
    public event Action<ulong>? MeasurementLogDropped;

    public MeasurementLogController(
        MainWindowViewModel viewModel,
        string? pendingLogPath,
        Func<string, decimal, IMeasurementResultSink> sinkFactory)
    {
        _viewModel = viewModel;
        _pendingLogPath = pendingLogPath;
        // A CLI --measurement-log launch logs for the whole session independently of
        // the persisted toggle (which is no longer forced true for the CLI path), so a
        // one-shot run cannot leak into the saved MeasurementLogEnabled setting.
        _cliLogging = pendingLogPath != null;
        _sinkFactory = sinkFactory;
        _previousRunState = viewModel.RunState;

        // No sink at construction: the log opens at run start so its lift-angle header matches
        // the angle the run uses. A CLI --measurement-log path stays pending until the first run.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Forwards a displayed frame to the active log sink (no-op when logging is off).</summary>
    public void ObserveDisplayed(AnalysisFrame frame) => _sink?.ObserveDisplayed(frame);

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _sink?.Dispose();
        _sink = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsMeasurementLogEnabled))
        {
            // Logging can only be toggled while stopped; disabling closes any open log.
            // An explicit user disable also ends CLI-session logging, so a --measurement-log
            // run the user turned off does not silently resume on the next run start.
            if (!_viewModel.IsMeasurementLogEnabled)
            {
                _cliLogging = false;
                CloseSink();
            }

            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.RunState))
        {
            RunUiState previous = _previousRunState;
            RunUiState current = _viewModel.RunState;
            _previousRunState = current;

            // Open a fresh log only at a real run start (e.g. Stopped -> Running), not when
            // resuming from Paused, so each run records its own lift angle while a pause/resume
            // keeps appending to the same file.
            if (current == RunUiState.Running &&
                previous != RunUiState.Paused &&
                (_viewModel.IsMeasurementLogEnabled || _cliLogging))
            {
                OpenSink();
            }

            // A completed or failed stop ends the current run's evidence file.
            // Pause/resume keeps the sink open; the next real run opens a fresh file.
            if (current is RunUiState.Stopped or RunUiState.StopFailed)
            {
                CloseSink();
            }
        }
    }

    private void OpenSink()
    {
        _sink?.Dispose();
        _sink = _sinkFactory(NextLogPath(), _viewModel.LiftAngle);
    }

    private void CloseSink()
    {
        IMeasurementResultSink? sink = _sink;
        _sink = null;
        if (sink == null)
        {
            return;
        }

        // If the writer fell behind and dropped rows, the saved CSV is incomplete; capture
        // the count before disposing and surface it so the loss is observable rather than silent.
        ulong dropped = sink.DroppedEntries;
        sink.Dispose();
        if (dropped > 0)
        {
            MeasurementLogDropped?.Invoke(dropped);
        }
    }

    // The CLI --measurement-log path opens the first run's log; later runs get a
    // timestamped file under the executable's log/ folder.
    private string NextLogPath()
    {
        if (_pendingLogPath is string path)
        {
            _pendingLogPath = null;
            return LogFilePaths.EnsureParentDirectory(path);
        }

        string baseDirectory = AppContext.BaseDirectory;
        Directory.CreateDirectory(Path.Combine(baseDirectory, "log"));
        return BuildLogPath(baseDirectory, DateTime.Now);
    }

    internal static string BuildLogPath(string baseDirectory, DateTime timestamp) =>
        // Sub-second precision so two default-path runs started within the same wall-clock
        // second cannot resolve to the same file (the sink opens with append:false, so a
        // collision would truncate the earlier run's log).
        Path.Combine(baseDirectory, "log", timestamp.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture) + ".csv");
}
