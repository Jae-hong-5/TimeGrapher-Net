namespace TimeGrapher.App.Services;

internal enum RunCommandMode
{
    Unknown,
    Live,
    Playback,
    Simulation,
}

internal static class RunCommandModePolicies
{
    public static bool AllowsSelectableSampleRate(RunCommandMode mode)
    {
        return mode is RunCommandMode.Live or RunCommandMode.Simulation;
    }

    public static bool AllowsGain(RunCommandMode mode)
    {
        return mode == RunCommandMode.Live;
    }
}
