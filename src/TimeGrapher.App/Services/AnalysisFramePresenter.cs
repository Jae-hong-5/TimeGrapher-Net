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
    // Last error-log detail written, used to suppress the per-frame disk write for a
    // persistent warning state (deadline degradation / signal-quality / no-signal):
    // such a state reports the same detail every active-tab frame and would grow the
    // log unbounded. Logging once per state-change (when the detail changes) keeps the
    // visible status text update untouched.
    private string? _lastLoggedDetail;

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
        // Grow the review scrub range to the newest captured reading. This is the only
        // stopped-safe mutation: it extends the review range without touching the
        // terminal status, latency readout, or run-status report.
        _viewModel.UpdateReviewMaximum(frame.MetricsHistory?.LatestTimeS ?? 0.0);

        // A drained final frame of a completed run arrives after the GUI reached Stopped
        // (completeInput keeps the session id alive). It must neither re-raise the waiting
        // overlay nor overwrite the terminal status/latency/report the stop already set.
        if (_viewModel.RunState == RunUiState.Stopped)
        {
            return;
        }

        _viewModel.IsAwaitingBeatSync = !frame.BeatSynced;

        _latencyStats.Observe(frame, droppedFrames, displayTicks);
        if (_latencyStats.TryFormatStatus(displayTicks) is string latencyText)
        {
            _viewModel.LatencyText = latencyText;
        }

        AnalysisRunStatusReporter.Report report = _statusReporter.Describe(frame, droppedFrames, sampleRate);
        if (report.StatusText != null)
        {
            if (report.LogDetail != null)
            {
                _viewModel.SetWarningStatus(report.StatusText);
            }
            else
            {
                _viewModel.StatusText = report.StatusText;
            }

            if (report.LogDetail != null && report.LogDetail != _lastLoggedDetail)
            {
                _lastLoggedDetail = report.LogDetail;
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
        _lastLoggedDetail = null;
        _viewModel.LatencyText = "";
        _viewModel.ResetReview();
    }
}
