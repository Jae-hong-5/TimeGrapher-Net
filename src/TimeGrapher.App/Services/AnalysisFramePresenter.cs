using System;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Maps a rendered analysis frame onto view-model state — the non-rendering half of the
/// MainWindow's frame handling. Owns the stateful latency/status trackers and pushes
/// awaiting-beat-sync, the review scrub maximum, the latency readout, and the run-status text
/// (with its error-log/console side effects) to the view-model. The View keeps the ScottPlot
/// rendering, the frame routing, the session-id gate, and the displayed-frame observers.
/// </summary>
internal sealed class AnalysisFramePresenter
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IUserErrorLog _errorLog;
    private readonly LatencyStatsTracker _latencyStats = new();
    private readonly AnalysisRunStatusReporter _statusReporter = new();

    public AnalysisFramePresenter(MainWindowViewModel viewModel, IUserErrorLog errorLog)
    {
        _viewModel = viewModel;
        _errorLog = errorLog;
    }

    /// <summary>
    /// Applies a frame's view-model-facing updates. Called by the View after it has rendered the
    /// frame and stamped the display timestamp (the latency display leg), passing the frame's
    /// effective sample rate.
    /// </summary>
    public void Present(AnalysisFrame frame, ulong droppedFrames, long displayTicks, int sampleRate)
    {
        // The drained final frame of a completed run arrives after the GUI reached Stopped
        // (completeInput keeps the session id alive); it must not re-raise the waiting overlay.
        if (_viewModel.RunState != RunUiState.Stopped)
        {
            _viewModel.IsAwaitingBeatSync = !frame.BeatSynced;
        }

        // Grow the review scrub range to the newest captured reading.
        _viewModel.UpdateReviewMaximum(frame.MetricsHistory?.LatestTimeS ?? 0.0);

        _latencyStats.Observe(frame, droppedFrames, displayTicks);
        if (_latencyStats.TryFormatStatus(displayTicks) is string latencyText)
        {
            _viewModel.LatencyText = latencyText;
        }

        AnalysisRunStatusReporter.Report report = _statusReporter.Describe(frame, droppedFrames, sampleRate);
        if (report.StatusText != null)
        {
            _viewModel.StatusText = report.StatusText;
            if (report.LogDetail != null)
            {
                _errorLog.Write(report.StatusText, report.LogDetail);
            }
        }

        if (report.ConsoleWarning != null)
        {
            Console.Error.WriteLine(report.ConsoleWarning);
        }
    }

    /// <summary>Resets the trackers and the view-model's latency/review state for a new session.</summary>
    public void Reset()
    {
        _statusReporter.Reset();
        _latencyStats.Reset();
        _viewModel.LatencyText = "";
        _viewModel.ResetReview();
    }
}
