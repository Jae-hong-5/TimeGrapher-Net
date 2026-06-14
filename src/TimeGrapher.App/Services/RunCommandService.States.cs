namespace TimeGrapher.App.Services;

internal sealed partial class RunCommandService
{
    private interface IRunCommandState
    {
        Task StartAsync(RunCommandService service);

        void TogglePause(RunCommandService service);

        void StopRunWithoutReset(RunCommandService service);

        void Reset(RunCommandService service);
    }

    private abstract class RunCommandState : IRunCommandState
    {
        public virtual Task StartAsync(RunCommandService service)
        {
            _ = service;
            return Task.CompletedTask;
        }

        public virtual void TogglePause(RunCommandService service)
        {
            _ = service;
        }

        public virtual void StopRunWithoutReset(RunCommandService service)
        {
            _ = service;
        }

        public virtual void Reset(RunCommandService service)
        {
            _ = service;
        }
    }

    private sealed class StoppedState : RunCommandState
    {
        public static StoppedState Instance { get; } = new();

        public override Task StartAsync(RunCommandService service)
        {
            return service.StartFromStoppedAsync();
        }

        public override void Reset(RunCommandService service)
        {
            service.ResetStopped();
        }
    }

    private sealed class StartingState : RunCommandState
    {
        public static StartingState Instance { get; } = new();
    }

    private sealed class RunningState : RunCommandState
    {
        public static RunningState Instance { get; } = new();

        public override void TogglePause(RunCommandService service)
        {
            service.PauseRunning();
        }

        public override void StopRunWithoutReset(RunCommandService service)
        {
            service.StopOnly();
        }
    }

    private sealed class PausedState : RunCommandState
    {
        public static PausedState Instance { get; } = new();

        public override void TogglePause(RunCommandService service)
        {
            service.ResumePaused();
        }

        public override void StopRunWithoutReset(RunCommandService service)
        {
            service.StopOnly();
        }

        public override void Reset(RunCommandService service)
        {
            service.ResetFromPaused();
        }
    }

    private sealed class StoppingState : RunCommandState
    {
        public static StoppingState Instance { get; } = new();

        public override void StopRunWithoutReset(RunCommandService service)
        {
            service.RetryPendingStop();
        }

        public override void Reset(RunCommandService service)
        {
            service.RetryPendingStop();
        }
    }

    private sealed class StopFailedState : RunCommandState
    {
        public static StopFailedState Instance { get; } = new();

        public override void StopRunWithoutReset(RunCommandService service)
        {
            service.RetryPendingStop();
        }

        public override void Reset(RunCommandService service)
        {
            service.RetryPendingStop();
        }
    }
}
