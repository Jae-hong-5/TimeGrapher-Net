using System;
using System.Threading.Tasks;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private sealed class RunCommandOperations : IRunCommandOperations
    {
        private readonly MainWindow _owner;

        public RunCommandOperations(MainWindow owner)
        {
            _owner = owner;
        }

        public bool IsClosing => _owner.mIsClosing;

        public bool HasActiveWorker => _owner.mRunSessionController.HasActiveInputWorker;

        public RunCommandMode CurrentMode
        {
            get => _owner.CurrentMode();
        }

        public void ConfigureLiveAudio()
        {
            _owner.ConfigureSoundCard();
        }

        public Task<bool> StartLiveAsync()
        {
            return _owner.LiveStart();
        }

        public Task<bool> StartPlaybackAsync()
        {
            return _owner.PlaybackStart();
        }

        public Task<bool> StartSimulationAsync()
        {
            return _owner.SimStart();
        }

        public void SetWorkersPaused(bool paused)
        {
            _owner.SetWorkersPaused(paused);
        }

        public void CleanupFailedStart()
        {
            _owner.InvalidateRunSession();
            _owner.mRunSessionController.StopInputWorker("Input");
            _owner.mRunSessionController.StopAnalysisThread();
            _owner.AudioCloseCheck();
        }

        public Task ShowStartFailureAsync(Exception exception)
        {
            return _owner.mDialogs.ShowErrorAsync("Error", "Failed to start: " + exception.Message);
        }

        public RunCommandStopOutcome StopLive()
        {
            RunSessionStopOutcome outcome = CombineStopOutcome(
                _owner.StopAudioThread(),
                _owner.mRunSessionController.StopAnalysisThread());
            return MapStopOutcome(outcome);
        }

        public RunCommandStopOutcome StopPlayback()
        {
            RunSessionStopOutcome outcome = CombineStopOutcome(
                _owner.StopPlaybackThread(),
                _owner.mRunSessionController.StopAnalysisThread());
            return MapStopOutcome(outcome);
        }

        public RunCommandStopOutcome StopSimulation()
        {
            RunSessionStopOutcome outcome = CombineStopOutcome(
                _owner.StopSimThread(),
                _owner.mRunSessionController.StopAnalysisThread());
            return MapStopOutcome(outcome);
        }

        public bool CloseAudio()
        {
            return _owner.AudioCloseCheck();
        }

        public void InvalidateRunSession()
        {
            _owner.InvalidateRunSession();
        }

        public void RestorePlaybackOrSimulationAudioState()
        {
            _owner.RestorePlaybackOrSimulationAudioState();
        }

        private static RunCommandStopOutcome MapStopOutcome(RunSessionStopOutcome outcome)
        {
            return outcome == RunSessionStopOutcome.Stopped
                ? RunCommandStopOutcome.Stopped
                : RunCommandStopOutcome.Stopping;
        }
    }
}
