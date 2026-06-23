using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Owns the optional measurement-result CSV log: reacts to the IsMeasurementLogEnabled toggle by
/// creating or disposing the sink, consumes the CLI --measurement-log path once, and forwards
/// displayed frames. The flow the MainWindow code-behind used to own; mirrors the other
/// view-model-subscriber controllers. The sink is created through an injected factory so the
/// controller is testable without opening a file.
/// </summary>
internal sealed class MeasurementLogController : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<string, decimal, IMeasurementResultSink> _sinkFactory;
    private string? _pendingLogPath;
    private IMeasurementResultSink? _sink;

    public MeasurementLogController(
        MainWindowViewModel viewModel,
        string? pendingLogPath,
        Func<string, decimal, IMeasurementResultSink> sinkFactory)
    {
        _viewModel = viewModel;
        _pendingLogPath = pendingLogPath;
        _sinkFactory = sinkFactory;

        // Configure to match the enable state already seeded on the view-model, then subscribe
        // (the code-behind's ctor ordering: an enabled CLI session opens the log up front).
        Configure(_viewModel.IsMeasurementLogEnabled);
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
            Configure(_viewModel.IsMeasurementLogEnabled);
        }
    }

    private void Configure(bool enabled)
    {
        _sink?.Dispose();
        _sink = enabled ? _sinkFactory(NextLogPath(), _viewModel.LiftAngle) : null;
    }

    // The CLI --measurement-log path opens the first enabled session; later sessions get a
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
        Path.Combine(baseDirectory, "log", timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
}
