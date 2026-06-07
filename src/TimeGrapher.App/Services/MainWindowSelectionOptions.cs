namespace TimeGrapher.App.Services;

internal sealed class MainWindowSelectionOptions
{
    public MainWindowSelectionOptions(
        string playbackSourceName,
        string simulationSourceName,
        IReadOnlyList<string> preferredLiveDeviceNames,
        IReadOnlyList<int> averagingPeriods)
    {
        PlaybackSourceName = playbackSourceName;
        SimulationSourceName = simulationSourceName;
        PreferredLiveDeviceNames = preferredLiveDeviceNames;
        AveragingPeriods = averagingPeriods;
    }

    public string PlaybackSourceName { get; }

    public string SimulationSourceName { get; }

    public IReadOnlyList<string> PreferredLiveDeviceNames { get; }

    public IReadOnlyList<int> AveragingPeriods { get; }
}
