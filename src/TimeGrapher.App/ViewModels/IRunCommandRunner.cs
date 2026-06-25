using System.Threading.Tasks;

namespace TimeGrapher.App.ViewModels;

/// <summary>
/// The run-command actions the play/pause, stop and reset commands invoke. Implemented by the
/// run-command service and attached to the view-model after construction (the service
/// needs the view-model, so the view-model cannot constructor-inject it). Lets the
/// commands carry their own bodies instead of the View passing in Func/Action delegates.
/// </summary>
internal interface IRunCommandRunner
{
    Task StartAsync();

    void TogglePause();

    void StopRunWithoutReset();

    void Reset();
}
