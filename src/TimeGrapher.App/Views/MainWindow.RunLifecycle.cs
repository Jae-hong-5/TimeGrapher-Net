using System;
using System.Globalization;
using System.Threading.Tasks;

using Avalonia.Threading;

using TimeGrapher.App.Audio;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.AudioIo;
using TimeGrapher.Core.Shared;
using TimeGrapher.Core.Sim;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // ~MainWindow: StopAnalysisThread(); plus stop any running input worker.
        mIsClosing = true;
        mViewModel.PropertyChanged -= mSelectionCoordinator.OnViewModelPropertyChanged;
        mRunControlController.Detach();
        mAcceptBandController.Detach();
        mSamplingSettingsController.Detach();
        mAppSettingsController.Detach();
        AppSettingsStore.Flush();
        mViewModel.PropertyChanged -= OnReviewCursorPropertyChanged;
        // Final close has no retry surface, so stop the input and analysis workers with a
        // bounded stop and dispose them; a worker that times out is abandoned to
        // process-exit teardown (its Dispose() would otherwise block the close forever on
        // an unbounded join) rather than kept for a retry that can never come at close.
        mRunSessionController.CloseBlocking();
        mAnalysisPerformanceLogger?.Dispose();
        mMeasurementLogController.MeasurementLogDropped -= OnMeasurementLogDropped;
        mMeasurementLogController.Dispose();
        if (!AudioCloseCheck() && mWavWriter != null)
        {
            // Final shutdown: the retry surface is gone with the window, so give the
            // recording one last bounded close attempt (Dispose re-runs Close with
            // its 5s join) and release it regardless.
            mWavWriter.Dispose();
            mWavWriter = null;
        }
    }

    private void InvalidateRunSession()
    {
        mRunSessionController.InvalidateRunSession();
    }

    private bool TryResolveLiveStartSelection(
        out int deviceNumber,
        out int sampleRate,
        out string userMessage,
        out string logDetail)
    {
        deviceNumber = CurrentInputDeviceNumber();
        sampleRate = 0;
        if (deviceNumber < 0)
        {
            userMessage = UserErrorMessages.SelectLiveAudioDevice;
            logDetail = "Live start rejected because no live audio device is selected.";
            return false;
        }

        if (!mRunSelectionResolver.TryGetSelectedSampleRate(mAudioSelection.AvailableSampleRates, mAudioSelection.AvailableSampleRateCount, out sampleRate))
        {
            userMessage = UserErrorMessages.SelectSampleRate;
            logDetail = "Live start rejected because no valid sample rate is selected.";
            return false;
        }

        userMessage = string.Empty;
        logDetail = string.Empty;
        return true;
    }

    private void StartAudioThread(int deviceNumber, int sampleRate)
    {
        mAudioSelection.CurrentSampleRate = sampleRate;
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(sampleRate, out ulong runSessionToken);

        ILiveAudioWorker audioWorker = LiveAudioBackend.CreateWorker(buffer);
        Action captureEndedHandler = () => OnLiveCaptureEnded(runSessionToken);
        audioWorker.CaptureEnded += captureEndedHandler;
        mRunSessionController.AttachInputWorker(audioWorker, runSessionToken, () => audioWorker.CaptureEnded -= captureEndedHandler);
        // Normalize at the boundary so an out-of-range/off-step value can never reach the
        // capture buffer (the controller already snaps on edit).
        audioWorker.Start(
            deviceNumber,
            sampleRate,
            (float)(mViewModel.Gain / 1000.0),
            SamplingSettings.NormalizeCaptureBufferMs(mViewModel.CaptureBufferMs));
    }

    private void OnLiveCaptureEnded(ulong runSessionToken)
    {
        // Fires on the capture thread; marshal to the UI thread.
        Dispatcher.UIThread.Post(() => HandleLiveCaptureEnded(runSessionToken));
    }

    private void HandleLiveCaptureEnded(ulong runSessionToken)
    {
        if (!mRunSessionController.IsCurrentRunSession(runSessionToken))
        {
            return;
        }

        mRunCommandService.StopRunAndRefreshDevices();

        // If the stop could not complete, StopRunAndRefreshDevices already set
        // StopFailed and "press Reset to retry"; overwriting it with the
        // capture-ended notice would hide the recovery instruction behind a
        // now-secondary message. Only report the capture-ended cause when the
        // stop actually succeeded.
        if (mViewModel.RunState != RunUiState.StopFailed)
        {
            ReportUserErrorStatus(
                UserErrorMessages.LiveAudioStopped,
                "Live capture ended unexpectedly for run session " +
                runSessionToken.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private RunSessionStopOutcome StopAudioThread()
    {
        // LocalStopAudio -> StopAudioRecording.
        return mRunSessionController.StopInputWorker("Audio");
    }

    private void StartPlaybackThread(string fileName)
    {
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(mAudioSelection.CurrentSampleRate, out ulong runSessionToken);

        var playbackWorker = new PlaybackWorker(buffer, mAudioSelection.CurrentSampleRate);
        Action<PlaybackCompletionReason> doneHandler = reason => OnPlaybackDoneReadingFile(runSessionToken, reason);
        playbackWorker.DoneReadingFile += doneHandler;
        mRunSessionController.AttachInputWorker(playbackWorker, runSessionToken, () => playbackWorker.DoneReadingFile -= doneHandler);
        if (!playbackWorker.Start(fileName))
        {
            throw new InvalidOperationException("Playback worker is already running.");
        }
    }

    private void StartSimThread(WatchSynthStreamConfig cfg)
    {
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(mAudioSelection.CurrentSampleRate, out ulong runSessionToken);

        var simWorker = new SimWorker(buffer, mAudioSelection.CurrentSampleRate);
        Action<SimCompletionReason> doneHandler = reason => OnSimDone(runSessionToken, reason);
        simWorker.SimDone += doneHandler;
        mRunSessionController.AttachInputWorker(simWorker, runSessionToken, () => simWorker.SimDone -= doneHandler);
        if (!simWorker.Start(cfg))
        {
            throw new InvalidOperationException("Sim worker is already running.");
        }
    }

    private RunSessionStopOutcome StopPlaybackThread()
    {
        return mRunSessionController.StopInputWorker("Playback");
    }

    private RunSessionStopOutcome StopSimThread()
    {
        return mRunSessionController.StopInputWorker("Sim");
    }

    private void OnPlaybackDoneReadingFile(ulong runSessionToken, PlaybackCompletionReason reason)
    {
        // PlaybackDoneReadingFile fires on the playback thread; marshal to UI thread.
        Dispatcher.UIThread.Post(() => HandlePlaybackDoneReadingFile(runSessionToken, reason));
    }

    private void OnSimDone(ulong runSessionToken, SimCompletionReason reason)
    {
        Dispatcher.UIThread.Post(() => HandleSimDone(runSessionToken, reason));
    }

    private void HandlePlaybackDoneReadingFile(ulong runSessionToken, PlaybackCompletionReason reason)
    {
        CompletePlaybackOrSimulationRun(
            runSessionToken,
            shouldRestoreAudioState: CurrentMode() == RunCommandMode.Playback,
            stopInputWorker: () => mRunSessionController.StopInputWorker("Playback"),
            failureStatus: UserErrorMessages.PlaybackStoppedWithError,
            failureDetail: "Playback worker completed with failure.",
            failed: reason == PlaybackCompletionReason.Failed);
    }

    private void HandleSimDone(ulong runSessionToken, SimCompletionReason reason)
    {
        CompletePlaybackOrSimulationRun(
            runSessionToken,
            shouldRestoreAudioState: CurrentMode() == RunCommandMode.Simulation,
            stopInputWorker: () => mRunSessionController.StopInputWorker("Sim"),
            failureStatus: UserErrorMessages.SimulationStoppedWithError,
            failureDetail: "Simulation worker completed with failure.",
            failed: reason == SimCompletionReason.Failed);
    }

    private void CompletePlaybackOrSimulationRun(
        ulong runSessionToken,
        bool shouldRestoreAudioState,
        Func<RunSessionStopOutcome> stopInputWorker,
        string failureStatus,
        string failureDetail,
        bool failed)
    {
        if (!mRunSessionController.IsCurrentRunSession(runSessionToken))
        {
            return;
        }

        InvalidateRunSession();
        SetGuiStoppingMode();
        if (shouldRestoreAudioState)
        {
            RestorePlaybackOrSimulationAudioState();
        }

        RunSessionStopOutcome inputOutcome = stopInputWorker();
        RunSessionStopOutcome analysisOutcome = mRunSessionController.StopAnalysisThread(completeInput: true);
        RunSessionStopOutcome outcome = CombineStopOutcome(inputOutcome, analysisOutcome);
        bool audioClosed = outcome != RunSessionStopOutcome.Stopping && AudioCloseCheck();
        bool finalFrameQueued = analysisOutcome == RunSessionStopOutcome.Stopped;
        bool measurementMayBeIncomplete = analysisOutcome == RunSessionStopOutcome.StoppedIncomplete;

        if (outcome == RunSessionStopOutcome.Stopping || !audioClosed)
        {
            // A worker timeout or recording-close failure during natural
            // completion must surface StopFailed (the manual-stop failure state),
            // not leave the app stuck in Stopping. If CompleteInput already queued
            // a final frame, preserve the same render-before-log-close ordering as
            // the successful EOF path.
            Action finishFailed = () => FinishFailedPlaybackOrSimulationRun(
                outcome,
                inputOutcome,
                analysisOutcome,
                audioClosed,
                failed);
            if (finalFrameQueued)
            {
                Dispatcher.UIThread.Post(finishFailed);
            }
            else
            {
                finishFailed();
            }

            return;
        }

        // CompleteInput publishes the final analysis frame through the UI scheduler.
        // Queue the visible Stopped transition behind that render so displayed-frame
        // observers (notably measurement CSV logging) see the final frame before
        // RunState closes the run's log sink. The incomplete fallback path may not
        // have a final frame; posting is still harmless and keeps one ordering rule.
        Dispatcher.UIThread.Post(() => FinishCompletedPlaybackOrSimulationRun(
            failed,
            failureStatus,
            failureDetail,
            measurementMayBeIncomplete));
    }

    private void FinishFailedPlaybackOrSimulationRun(
        RunSessionStopOutcome outcome,
        RunSessionStopOutcome inputOutcome,
        RunSessionStopOutcome analysisOutcome,
        bool audioClosed,
        bool inputFailed)
    {
        // RunCommandService derives its state from the view model, so RESET then
        // retries via StopFailedState.
        mViewModel.SetStopFailed();
        ReportUserErrorStatus(
            UserErrorMessages.StopDidNotFinish,
            "Run completion stop failed: outcome=" + outcome +
            ", input_outcome=" + inputOutcome +
            ", analysis_outcome=" + analysisOutcome +
            ", audio_closed=" + audioClosed +
            ", input_failed=" + inputFailed + ".");
    }

    private void FinishCompletedPlaybackOrSimulationRun(
        bool failed,
        string failureStatus,
        string failureDetail,
        bool measurementMayBeIncomplete)
    {
        // Cleared before SetGuiStopMode, which transitions RunState to Stopped and may
        // raise OnMeasurementLogDropped synchronously (setting the flag) while the log closes.
        mMeasurementLogDroppedThisStop = false;
        SetGuiStopMode();
        mViewModel.IsAwaitingBeatSync = false;
        if (failed)
        {
            ReportUserErrorStatus(failureStatus, failureDetail);
        }
        else if (measurementMayBeIncomplete)
        {
            ReportUserErrorStatus(
                UserErrorMessages.MeasurementLogMayBeIncomplete,
                "Analysis drain timed out at natural run completion; final measurement rows may be incomplete.");
        }
        else if (!mMeasurementLogDroppedThisStop)
        {
            mViewModel.StatusText = "Stopped";
        }

        // If mMeasurementLogDroppedThisStop is set, OnMeasurementLogDropped already set the
        // incomplete-log warning status during SetGuiStopMode; leave it rather than overwrite.
    }

    private async Task<bool> RecordSessionCheck(int sampleRate)
    {
        // A writer left over from a failed close must never leak into a new run.
        if (!AudioCloseCheck())
        {
            return false;
        }

        RecordingSessionStartResult result = await mRecordingSessionService.TryStartAsync(sampleRate);
        if (result.Writer != null)
        {
            mWavWriter = result.Writer;
        }

        return result.ShouldContinue;
    }

    // Set when the measurement-log drop warning was raised during the current stop, so the
    // trailing success "Stopped" status does not overwrite it. Cleared at the start of the
    // completed-run handler, before the stop transition can raise the event.
    private bool mMeasurementLogDroppedThisStop;

    private void OnMeasurementLogDropped(ulong droppedEntries)
    {
        // The measurement-log writer fell behind and dropped rows, so the saved CSV is
        // incomplete. Surface it on the same channel as the analysis-drain and WAV-drop
        // warnings instead of leaving the loss silent. The error-log write is the durable
        // record; the flag protects the warning status from the trailing "Stopped" set.
        mMeasurementLogDroppedThisStop = true;
        ReportUserErrorStatus(
            UserErrorMessages.MeasurementLogMayBeIncomplete,
            "Measurement log dropped " +
            droppedEntries.ToString(CultureInfo.InvariantCulture) +
            " row(s) under writer backpressure.");
    }

    private bool AudioCloseCheck()
    {
        if (mWavWriter != null)
        {
            ulong droppedBlocks = mWavWriter.DroppedBlocks;
            bool closed = mWavWriter.Close();
            if (!closed)
            {
                ReportUserErrorStatus(
                    UserErrorMessages.RecordingCloseFailed,
                    "WAV writer close returned false. is_open=" + mWavWriter.IsOpen + ".");
                if (mWavWriter.IsOpen)
                {
                    // Retryable: the writer thread has not finished yet; a Stop
                    // retry re-attempts the close.
                    return false;
                }

                // Terminal: the writer already tore down, so a retry has nothing
                // left to redo. Release it now so no stale writer can leak into a
                // later run; the failure still surfaces once via the status text.
                mWavWriter.Dispose();
                mWavWriter = null;
                return false;
            }

            mWavWriter.Dispose();
            mWavWriter = null;
            if (droppedBlocks != 0)
            {
                ReportUserErrorStatus(
                    UserErrorMessages.RecordingMayBeIncomplete,
                    "WAV recording dropped " +
                    droppedBlocks.ToString(CultureInfo.InvariantCulture) +
                    " block(s).");
            }
        }

        return true;
    }

    private void SetGuiRunMode()
    {
        mViewModel.SetRunning();
    }

    private void SetGuiStoppingMode()
    {
        mViewModel.SetStopping();
    }

    private void SetGuiStopMode()
    {
        RunCommandMode mode = CurrentMode();
        mViewModel.SetModeAllowsSampleRate(RunCommandModePolicies.AllowsSelectableSampleRate(mode));
        mViewModel.SetModeAllowsGain(RunCommandModePolicies.AllowsGain(mode));
        mViewModel.SetModeAllowsSimulationParameters(RunCommandModePolicies.AllowsSimulationParameters(mode));
        mViewModel.SetStopped();
    }

    private async Task<bool> LiveStart()
    {
        if (!TryResolveLiveStartSelection(
                out int deviceNumber,
                out int sampleRate,
                out string userMessage,
                out string logDetail))
        {
            await ShowUserErrorAsync(userMessage, logDetail);
            return false;
        }

        if (!await RecordSessionCheck(sampleRate))
        {
            return false;
        }

        try
        {
            StartAudioThread(deviceNumber, sampleRate);
        }
        catch (Exception ex)
        {
            InvalidateRunSession();
            mRunSessionController.StopInputWorker("Audio");
            mRunSessionController.StopAnalysisThread();
            AudioCloseCheck();
            await ShowUserErrorAsync(UserErrorMessages.CouldNotStartLiveAudio, ex.ToString());
            return false;
        }

        SetGuiRunMode();
        mViewModel.StatusText = "Running";
        return true;
    }

    private async Task<bool> PlaybackStart()
    {
        PlaybackFileSelectionResult selection = await mPlaybackFileService.SelectPlaybackFileAsync(mCurrentDir);
        if (!selection.Selected || selection.FilePath == null)
        {
            if (!string.IsNullOrEmpty(selection.StatusMessage))
            {
                mViewModel.StatusText = selection.StatusMessage;
            }

            return false;
        }

        mCurrentDir = selection.CurrentDirectory;
        GetAudioRate(out mRateBeforePlaybackOrSim);
        GetAudioDevice(out mDeviceNameBeforePlaybackOrSim);
        if (!SetAudioDevice(PLAYBACK_SOURCE))
        {
            Console.Error.WriteLine("SetAudioDevice Failed");
        }

        if (!SetAudioRate(selection.SampleRate))
        {
            Console.Error.WriteLine("SetAudioRate Failed");
            RestorePlaybackOrSimulationAudioState();
            return false;
        }

        // Playback replays an existing WAV, so there is nothing to record:
        // skip the record-session prompt but keep the leaked-writer guard.
        if (!AudioCloseCheck())
        {
            RestorePlaybackOrSimulationAudioState();
            return false;
        }

        try
        {
            StartPlaybackThread(selection.FilePath);
        }
        catch
        {
            // PrepareInputRun throws if a prior analysis worker is still stopping;
            // restore the pre-playback device/rate before the failure propagates,
            // matching the SetAudioRate/AudioCloseCheck failure paths above.
            RestorePlaybackOrSimulationAudioState();
            throw;
        }

        SetGuiRunMode();
        mViewModel.StatusText = "Running";
        return true;
    }

    private async Task<bool> SimStart()
    {
        WatchSynthStreamConfig cfg = mViewModel.Realistic
            ? WatchSynthStreamConfig.Realistic()
            : WatchSynthStreamConfig.Clean();

        SimulationSelection selection = mRunSelectionResolver.GetSimulationSelection(mAudioSelection.AvailableSampleRates, mAudioSelection.AvailableSampleRateCount);
        cfg.Bph = selection.Bph;
        cfg.SampleRateHz = (uint)selection.SampleRate;
        cfg.BeatErrorMs = -(double)mViewModel.SimBeatError;
        cfg.PcmPeakSignalLevel = SimulationAudioDefaults.PcmPeakSignalLevel;
        cfg.WatchAmplitudeDegrees = (double)mViewModel.SimAmplitude;
        cfg.LiftAngleDegrees = (double)mViewModel.LiftAngle;
        cfg.RateErrorSPerDay = (double)mViewModel.SimErrorRate;
        // Per-cluster A/B/C signal sizes (only effective with the realistic packet).
        cfg.AClusterLevelScale = (double)mViewModel.SimSignalAScale;
        cfg.BClusterLevelScale = (double)mViewModel.SimSignalBScale;
        cfg.CClusterLevelScale = (double)mViewModel.SimSignalCScale;

        if (!await RecordSessionCheck(selection.SampleRate))
        {
            return false;
        }

        GetAudioRate(out mRateBeforePlaybackOrSim);
        GetAudioDevice(out mDeviceNameBeforePlaybackOrSim);
        if (!SetAudioDevice(SIMULATION_SOURCE))
        {
            Console.Error.WriteLine("SetAudioDevice Failed");
        }

        if (!SetAudioRate(selection.SampleRate))
        {
            // Match PlaybackStart: a failed rate set must abort the start and restore
            // the prior device/rate rather than starting the sim on a wrong rate.
            Console.Error.WriteLine("SetAudioRate Failed");
            RestorePlaybackOrSimulationAudioState();
            return false;
        }

        try
        {
            StartSimThread(cfg);
        }
        catch
        {
            // PrepareInputRun throws if a prior analysis worker is still stopping;
            // restore the pre-simulation device/rate before the failure propagates.
            RestorePlaybackOrSimulationAudioState();
            throw;
        }

        SetGuiRunMode();
        mViewModel.StatusText = "Running";
        return true;
    }

    private void SetWorkersPaused(bool paused)
    {
        mRunSessionController.SetWorkersPaused(paused);
    }

    private void StopRunWithoutReset()
    {
        mRunCommandService.StopRunWithoutReset();
    }
}
