namespace TimeGrapher.App.Services;

/// <summary>
/// The auto-pause trigger the <see cref="RunControlController"/> uses when a watch-position
/// change should pause an active run. Implemented by <see cref="RunCommandService"/>, which
/// pauses only while running (a no-op otherwise).
/// </summary>
internal interface IRunCommandPause
{
    void PauseIfRunning();
}
