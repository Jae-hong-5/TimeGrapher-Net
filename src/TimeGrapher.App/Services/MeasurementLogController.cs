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
/// pause/resume, and closes the sink when logging is disabled or on dispose. Consumes the CLI
/// --measurement-log path for the first run, then timestamps later runs. The flow the MainWindow
/// code-behind used to own; mirrors the other view-model-subscriber controllers. The sink is
/// created through an injected factory so the controller is testable without opening a file.
/// </summary>
internal sealed class MeasurementLogController : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<string, decimal, IMeasurementResultSink> _sinkFactory;
    private string? _pendingLogPath;
    private IMeasurementResultSink? _sink;
    private RunUiState _previousRunState;

    public MeasurementLogController(
        MainWindowViewModel viewModel,
        string? pendingLogPath,
        Func<string, decimal, IMeasurementResultSink> sinkFactory)
    {
        _viewModel = viewModel;
        _pendingLogPath = pendingLogPath;
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
            if (!_viewModel.IsMeasurementLogEnabled)
            {
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
                _viewModel.IsMeasurementLogEnabled)
            {
                OpenSink();
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
        _sink?.Dispose();
        _sink = null;
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
