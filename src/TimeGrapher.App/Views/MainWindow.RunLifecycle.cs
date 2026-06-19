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
        mViewModel.PropertyChanged -= OnRunControlPropertyChanged;
        mViewModel.PropertyChanged -= OnReviewCursorPropertyChanged;
        mRunSessionController.InvalidateRunSession();
        mRunSessionController.StopInputWorker("Input");
        mRunSessionController.StopAnalysisThread();
        mAnalysisPerformanceLogger?.Dispose();
        mMeasurementResultLogger?.Dispose();
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

    private bool TryResolveLiveStartSelection(out int deviceNumber, out int sampleRate, out string errorMessage)
    {
        deviceNumber = CurrentInputDeviceNumber();
        sampleRate = 0;
        if (deviceNumber < 0)
        {
            errorMessage = "No live audio device is selected.";
            return false;
        }

        if (!mRunSelectionResolver.TryGetSelectedSampleRate(mAvailableRates, mNumberOfRates, out sampleRate))
        {
            errorMessage = "No valid sample rate is selected.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private void StartAudioThread(int deviceNumber, int sampleRate)
    {
        mCurrentSamplesPerSecond = sampleRate;
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(sampleRate, out ulong runSessionToken);

        ILiveAudioWorker audioWorker = LiveAudioBackend.CreateWorker(buffer);
        Action captureEndedHandler = () => OnLiveCaptureEnded(runSessionToken);
        audioWorker.CaptureEnded += captureEndedHandler;
        mRunSessionController.AttachInputWorker(audioWorker, runSessionToken, () => audioWorker.CaptureEnded -= captureEndedHandler);
        audioWorker.Start(deviceNumber, sampleRate, (float)(mViewModel.Gain / 1000.0));
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
            mViewModel.StatusText = "Live audio capture ended unexpectedly";
        }
    }

    private RunSessionStopOutcome StopAudioThread()
    {
        // LocalStopAudio -> StopAudioRecording.
        return mRunSessionController.StopInputWorker("Audio");
    }

    private void StartPlaybackThread(string fileName)
    {
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(mCurrentSamplesPerSecond, out ulong runSessionToken);

        var playbackWorker = new PlaybackWorker(buffer, mCurrentSamplesPerSecond);
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
        MasterAudioBuffer buffer = mRunSessionController.PrepareInputRun(mCurrentSamplesPerSecond, out ulong runSessionToken);

        var simWorker = new SimWorker(buffer, mCurrentSamplesPerSecond);
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
            failureStatus: "Playback failed",
            failed: reason == PlaybackCompletionReason.Failed);
    }

    private void HandleSimDone(ulong runSessionToken, SimCompletionReason reason)
    {
        CompletePlaybackOrSimulationRun(
            runSessionToken,
            shouldRestoreAudioState: CurrentMode() == RunCommandMode.Simulation,
            stopInputWorker: () => mRunSessionController.StopInputWorker("Sim"),
            failureStatus: "Simulation failed",
            failed: reason == SimCompletionReason.Failed);
    }

    private void CompletePlaybackOrSimulationRun(
        ulong runSessionToken,
        bool shouldRestoreAudioState,
        Func<RunSessionStopOutcome> stopInputWorker,
        string failureStatus,
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

        RunSessionStopOutcome outcome = stopInputWorker();
        outcome = CombineStopOutcome(outcome, mRunSessionController.StopAnalysisThread(completeInput: true));
        bool audioClosed = outcome == RunSessionStopOutcome.Stopped && AudioCloseCheck();
        if (outcome != RunSessionStopOutcome.Stopped || !audioClosed)
        {
            // A worker timeout or recording-close failure during natural
            // completion must surface StopFailed (the manual-stop failure state),
            // not leave the app stuck in Stopping. RunCommandService derives its
            // state from the view model, so RESET then retries via StopFailedState.
            mViewModel.SetStopFailed();
            mViewModel.StatusText = "Stop failed - press Reset to retry";
            return;
        }

        SetGuiStopMode();
        mViewModel.IsAwaitingBeatSync = false;
        mViewModel.StatusText = failed ? failureStatus : "Stopped";
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

    private bool AudioCloseCheck()
    {
        if (mWavWriter != null)
        {
            ulong droppedBlocks = mWavWriter.DroppedBlocks;
            bool closed = mWavWriter.Close();
            if (!closed)
            {
                mViewModel.StatusText = "Failed to close WAV recording cleanly";
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
                mViewModel.StatusText = "WAV recording dropped " +
                                     droppedBlocks.ToString(CultureInfo.InvariantCulture) +
                                     " block(s)";
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
        if (!TryResolveLiveStartSelection(out int deviceNumber, out int sampleRate, out string errorMessage))
        {
            mViewModel.StatusText = "Failed to start live audio";
            await mDialogs.ShowErrorAsync("Error", "Failed to start live audio: " + errorMessage);
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
            mViewModel.StatusText = "Failed to start live audio";
            await mDialogs.ShowErrorAsync("Error", "Failed to start live audio: " + ex.Message);
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
        // RealisticCheckBox -> realistic config; otherwise clean config
        // (MainWindow.cpp: watch_synth_stream_realistic_config / watch_synth_stream_clean_config).
        WatchSynthStreamConfig cfg = mViewModel.Realistic
            ? WatchSynthStreamConfig.Realistic()
            : WatchSynthStreamConfig.Clean();

        SimulationSelection selection = mRunSelectionResolver.GetSimulationSelection(mAvailableRates, mNumberOfRates);
        cfg.Bph = selection.Bph;
        cfg.SampleRateHz = (uint)selection.SampleRate;
        cfg.BeatErrorMs = -(double)mViewModel.SimBeatError;
        cfg.PcmPeakAmplitude = 0.40; // normalized float PCM digital output level
        cfg.WatchAmplitudeDegrees = (double)mViewModel.SimAmplitude;
        cfg.LiftAngleDegrees = (double)mViewModel.LiftAngle;
        cfg.RateErrorSPerDay = (double)mViewModel.SimErrorRate;

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
            Console.Error.WriteLine("SetAudioRate Failed");
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

    private async Task StartRunAsync()
    {
        await mRunCommandService.StartAsync();
    }

    private void TogglePauseRun()
    {
        mRunCommandService.TogglePause();
    }

    private void SetWorkersPaused(bool paused)
    {
        mRunSessionController.SetWorkersPaused(paused);
    }

    private void StopRunWithoutReset()
    {
        mRunCommandService.StopRunWithoutReset();
    }

    private void ResetRun()
    {
        mRunCommandService.Reset();
    }
}
