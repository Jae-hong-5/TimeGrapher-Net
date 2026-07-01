using TimeGrapher.Core.Analysis;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;

namespace TimeGrapher.App.Services;

internal enum RunSessionStopOutcome
{
    Stopped,
    StoppedIncomplete,
    Stopping,
}

internal sealed class RunSessionController : IDisposable, IRunSessionControls, IRunSessionLiveAdjustments
{
    // Bounded and deliberately short: the stop path joins the input and analysis worker threads
    // synchronously on the UI thread, so this caps how long a Stop/Reset can block the Avalonia
    // dispatcher per worker. A SIGKILL'd capture child and the block-boundary analysis stop both
    // finish well under this on a Pi 5; a genuinely wedged capture (uninterruptible ALSA/USB I/O,
    // which no longer timeout would recover) surfaces as StopFailed after ~0.8 s instead of
    // freezing the window for seconds, and the existing StopFailed retry path stays available.
    private const int WorkerStopTimeoutMs = 800;

    private readonly Func<ulong, AnalysisWorker.Config> _createAnalysisConfig;
    private readonly Action _resetBeforeRun;
    private readonly Action _clearPendingFrames;
    private readonly Action _resetRenderTiming;
    private readonly Action<AnalysisFrame> _onAnalysisFrameReady;
    private readonly Action<string> _setStatus;
    private readonly IUserErrorLog _errorLog;

    private MasterAudioBuffer? _rawAudio;
    private AnalysisWorker? _analysisWorker;
    private IAudioInputWorker? _inputWorker;
    private Action? _inputDataReadyHandler;
    private Action? _inputCompletionDetach;
    private ulong _runSessionToken;
    private int? _sweepMultiple;
    private WatchPosition? _activePosition;
    private bool? _sigmaAveraging;

    public RunSessionController(
        Func<ulong, AnalysisWorker.Config> createAnalysisConfig,
        Action resetBeforeRun,
        Action clearPendingFrames,
        Action resetRenderTiming,
        Action<AnalysisFrame> onAnalysisFrameReady,
        Action<string> setStatus,
        IUserErrorLog? errorLog = null)
    {
        _createAnalysisConfig = createAnalysisConfig;
        _resetBeforeRun = resetBeforeRun;
        _clearPendingFrames = clearPendingFrames;
        _resetRenderTiming = resetRenderTiming;
        _onAnalysisFrameReady = onAnalysisFrameReady;
        _setStatus = setStatus;
        _errorLog = errorLog ?? NullUserErrorLog.Instance;
    }

    public ulong AnalysisSessionId { get; private set; }

    public bool HasActiveInputWorker => _inputWorker != null;

    public MasterAudioBuffer PrepareInputRun(int sampleRate, out ulong runSessionToken)
    {
        runSessionToken = BeginRunSession();
        if (StopAnalysisThread() != RunSessionStopOutcome.Stopped)
        {
            // The previous analysis worker did not stop in time and is still
            // alive. Starting now would have StartAnalysisThread overwrite (and
            // leak) that worker handle and run two analysis threads at once. Fail
            // the start instead: StartAsync's catch routes to CleanupFailedStart,
            // and the worker stays addressable for a later stop/reset retry.
            throw new InvalidOperationException(
                "Previous analysis worker did not stop; cannot start a new run.");
        }

        _resetBeforeRun();

        _rawAudio = new MasterAudioBuffer(sampleRate);
        StartAnalysisThread();

        return _rawAudio;
    }

    public void AttachInputWorker(
        IAudioInputWorker worker,
        ulong runSessionToken,
        Action? detachCompletion = null)
    {
        _inputWorker = worker;
        _inputCompletionDetach = detachCompletion;
        _inputDataReadyHandler = CreateDataReadyHandler(runSessionToken);
        worker.DataReady += _inputDataReadyHandler;
    }

    public void InvalidateRunSession()
    {
        _ = BeginRunSession();
    }

    public bool IsCurrentRunSession(ulong runSessionToken)
    {
        return runSessionToken == _runSessionToken;
    }

    public RunSessionStopOutcome StopInputWorker(string workerName)
    {
        IAudioInputWorker? worker = _inputWorker;
        if (worker != null)
        {
            if (_inputDataReadyHandler != null)
            {
                worker.DataReady -= _inputDataReadyHandler;
                _inputDataReadyHandler = null;
            }

            // Detach the playback/sim/live completion handlers before the bounded
            // stop, not only on success: a worker that finishes during the stop or
            // does not stop in time must not fire a late completion that re-enters
            // the run lifecycle and bypasses the stop/reset intent.
            _inputCompletionDetach?.Invoke();
            _inputCompletionDetach = null;

            if (!worker.TryStop(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs)))
            {
                ReportStopTimeout(workerName + " worker did not stop within " + WorkerStopTimeoutMs + " ms.");
                return RunSessionStopOutcome.Stopping;
            }

            worker.Dispose();
            _inputWorker = null;
        }

        return RunSessionStopOutcome.Stopped;
    }

    public RunSessionStopOutcome StopAnalysisThread(bool completeInput = false)
    {
        if (_analysisWorker != null)
        {
            bool usedInterruptingFallback = false;
            bool stopped = completeInput
                ? _analysisWorker.CompleteInput(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs))
                : _analysisWorker.TryStop(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs));
            if (!stopped && completeInput)
            {
                // Natural playback/simulation completion first tries to drain and
                // publish the final frame. If the analysis backlog cannot drain
                // within the normal stop budget, fall back to an interrupting stop
                // so EOF behaves like a user Stop instead of trapping the UI in a
                // retry state with the log writer still open. Return a distinct
                // outcome so the caller can warn that final measurement evidence
                // may be incomplete.
                usedInterruptingFallback = true;
                stopped = _analysisWorker.TryStop(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs));
            }

            if (stopped)
            {
                _analysisWorker.AnalysisFrameReady -= _onAnalysisFrameReady;
                _analysisWorker.Dispose();
                _analysisWorker = null;
                if (completeInput)
                {
                    _resetRenderTiming();
                }
                else
                {
                    AnalysisSessionId++;
                    _clearPendingFrames();
                }
                return usedInterruptingFallback
                    ? RunSessionStopOutcome.StoppedIncomplete
                    : RunSessionStopOutcome.Stopped;
            }
            else
            {
                // Stop timed out: the worker thread is still alive and can keep
                // raising frames. Invalidate the analysis session now - detach the
                // handler, bump the id so any in-flight frame is discarded by the
                // SessionId gate, and clear the queue - so recovery does not render
                // stale frames. The worker object is kept for the retry/dispose.
                _analysisWorker.AnalysisFrameReady -= _onAnalysisFrameReady;
                AnalysisSessionId++;
                _clearPendingFrames();
                ReportStopTimeout("Analysis worker did not stop within " + WorkerStopTimeoutMs + " ms.");
                return RunSessionStopOutcome.Stopping;
            }
        }
        else
        {
            _clearPendingFrames();
        }

        return RunSessionStopOutcome.Stopped;
    }

    public void SetWorkersPaused(bool paused)
    {
        _inputWorker?.SetPaused(paused);
    }

    public void SetLiveInputVolume(float normalizedVolume)
    {
        if (_inputWorker is ILiveAudioWorker liveWorker)
        {
            liveWorker.SetVolume(normalizedVolume);
        }
    }

    /// <summary>
    /// Forwards the live-adjustable simulation knobs (rate error s/day, beat error ms,
    /// watch amplitude degrees, and the per-cluster A/B/C level scales) to the running
    /// sim worker. No-op unless a simulation run is active, mirroring
    /// <see cref="SetLiveInputVolume"/> for the live worker. The view model is the
    /// source of truth for the next run's start, so unlike the analysis-worker knobs
    /// there is nothing to remember here.
    /// </summary>
    public void SetLiveSimulationParameters(
        double rateErrorSPerDay,
        double beatErrorMs,
        double watchAmplitudeDegrees,
        double aClusterLevelScale,
        double bClusterLevelScale,
        double cClusterLevelScale)
    {
        if (_inputWorker is SimWorker simWorker)
        {
            simWorker.UpdateLiveParameters(
                rateErrorSPerDay,
                beatErrorMs,
                watchAmplitudeDegrees,
                aClusterLevelScale,
                bClusterLevelScale,
                cClusterLevelScale);
        }
    }

    /// <summary>Recolors the running analysis worker's sound print (no-op when idle).</summary>
    public void SetSoundBackgroundColor(uint backgroundColor)
    {
        _analysisWorker?.SetSoundBackgroundColor(backgroundColor);
    }

    /// <summary>
    /// Switches the running analysis worker's spectrogram colormap to match the UI
    /// theme (no-op when idle — a fresh run reads the current theme from config).
    /// </summary>
    public void SetSpectrogramColormap(bool light)
    {
        _analysisWorker?.SetSpectrogramColormap(light);
    }

    /// <summary>
    /// Forwards the Scope Sweep window multiple to the running analysis worker
    /// and remembers it so later runs start with the user's selection.
    /// </summary>
    public void SetSweepMultiple(int sweepMultiple)
    {
        _sweepMultiple = sweepMultiple;
        _analysisWorker?.SetSweepMultiple(sweepMultiple);
    }

    /// <summary>
    /// Forwards the active watch position to the running analysis worker
    /// and remembers it so later runs start with the user's selection.
    /// </summary>
    public void SetActivePosition(WatchPosition position)
    {
        _activePosition = position;
        _analysisWorker?.SetActivePosition(position);
    }

    /// <summary>
    /// Forwards the Beat Noise Scope 2 Σ averaging mode to the running analysis
    /// worker and remembers it so later runs start with the user's selection.
    /// </summary>
    public void SetSigmaAveraging(bool enabled)
    {
        _sigmaAveraging = enabled;
        _analysisWorker?.SetSigmaAveraging(enabled);
    }

    /// <summary>
    /// Final-close teardown: invalidate the session, then stop the input and analysis
    /// workers with the bounded <see cref="WorkerStopTimeoutMs"/> stop and dispose them.
    /// Worker <c>Dispose()</c> (and <see cref="AnalysisWorker.Stop"/>) perform an
    /// UNBOUNDED join/wait, so this disposes a worker only when its bounded stop actually
    /// succeeded; a worker that timed out is wedged (its threads are background and its
    /// capture process was already killed) and is abandoned to process-exit teardown
    /// rather than blocking the window close forever on an infinite join. Unlike the
    /// bounded <see cref="StopInputWorker"/> / <see cref="StopAnalysisThread"/> stops,
    /// there is no retry surface at close, so the wedged worker is not kept for a retry.
    /// </summary>
    public void CloseBlocking()
    {
        InvalidateRunSession();

        IAudioInputWorker? worker = _inputWorker;
        if (worker != null)
        {
            if (_inputDataReadyHandler != null)
            {
                worker.DataReady -= _inputDataReadyHandler;
                _inputDataReadyHandler = null;
            }

            _inputCompletionDetach?.Invoke();
            _inputCompletionDetach = null;

            // Stop with a bounded timeout. Dispose() performs an UNBOUNDED join/wait on
            // the worker thread or capture process, so disposing a worker that did not
            // stop in time would freeze the app close forever (the very hang the bounded
            // TryStop was meant to avoid). A timed-out worker is wedged and cannot be
            // force-joined; only dispose when it actually stopped, and abandon a wedged
            // worker (its threads are background and its capture process was already
            // killed) to process-exit teardown rather than blocking close.
            if (worker.TryStop(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs)))
            {
                worker.Dispose();
            }

            _inputWorker = null;
        }

        AnalysisWorker? analysisWorker = _analysisWorker;
        if (analysisWorker != null)
        {
            analysisWorker.AnalysisFrameReady -= _onAnalysisFrameReady;
            // Same reasoning as the input worker: Stop()/Dispose() join the analysis
            // thread with an infinite timeout, so only dispose when a bounded stop
            // succeeded and abandon a wedged (background) thread otherwise instead of
            // hanging the close.
            if (analysisWorker.TryStop(TimeSpan.FromMilliseconds(WorkerStopTimeoutMs)))
            {
                analysisWorker.Dispose();
            }

            _analysisWorker = null;
            AnalysisSessionId++;
        }

        _clearPendingFrames();
    }

    public void Dispose()
    {
        InvalidateRunSession();
        StopInputWorker("Input");
        StopAnalysisThread();
    }

    private ulong BeginRunSession()
    {
        unchecked
        {
            _runSessionToken++;
            if (_runSessionToken == 0)
            {
                _runSessionToken = 1;
            }

            return _runSessionToken;
        }
    }

    private void ReportStopTimeout(string detail)
    {
        _setStatus(UserErrorMessages.StopDidNotFinish);
        _errorLog.Write(UserErrorMessages.StopDidNotFinish, detail);
    }

    private void StartAnalysisThread()
    {
        AnalysisSessionId++;

        AnalysisWorker.Config analysisConfig = _createAnalysisConfig(AnalysisSessionId);

        _analysisWorker = new AnalysisWorker(_rawAudio!, analysisConfig);
        if (_sweepMultiple is int sweepMultiple)
        {
            _analysisWorker.SetSweepMultiple(sweepMultiple);
        }
        if (_activePosition is WatchPosition activePosition)
        {
            _analysisWorker.SetActivePosition(activePosition);
        }
        if (_sigmaAveraging is bool sigmaAveraging)
        {
            _analysisWorker.SetSigmaAveraging(sigmaAveraging);
        }
        _analysisWorker.AnalysisFrameReady += _onAnalysisFrameReady;
        _analysisWorker.Start();
    }

    private Action CreateDataReadyHandler(ulong runSessionToken)
    {
        return () =>
        {
            if (runSessionToken == _runSessionToken)
            {
                _analysisWorker?.NotifyDataReady();
            }
        };
    }
}
