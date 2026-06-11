namespace TimeGrapher.App.Services;

internal sealed class MainWindowSelectionOptions
{
    public MainWindowSelectionOptions(
        string playbackSourceName,
        string simulationSourceName)
    {
        PlaybackSourceName = playbackSourceName;
        SimulationSourceName = simulationSourceName;
    }

    public string PlaybackSourceName { get; }

    public string SimulationSourceName { get; }
}
