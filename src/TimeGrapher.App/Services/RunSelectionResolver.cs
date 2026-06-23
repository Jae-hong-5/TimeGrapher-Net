using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal readonly record struct AnalysisSelection(
    int AveragingPeriod,
    bool AutoBph,
    int ManualBph);

internal readonly record struct SimulationSelection(
    int Bph,
    int SampleRate);

internal sealed class RunSelectionResolver
{
    public const int DefaultAveragingPeriodSeconds = SamplingSettings.DefaultAveragingPeriodSeconds;
    public const int MinAveragingPeriodSeconds = SamplingSettings.AveragingPeriodFloorSeconds;
    public const int MaxAveragingPeriodSeconds = SamplingSettings.AveragingPeriodCeilingSeconds;
    public const int DefaultSimulationBph = 28800;

    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyList<int> _manualAutoBph;
    private readonly IReadOnlyList<int> _simulationBph;

    public RunSelectionResolver(
        MainWindowViewModel viewModel,
        IReadOnlyList<int> manualAutoBph,
        IReadOnlyList<int> simulationBph)
    {
        _viewModel = viewModel;
        _manualAutoBph = manualAutoBph;
        _simulationBph = simulationBph;
    }

    public int DefaultSimulationBphIndex => FindValue(_simulationBph, DefaultSimulationBph);

    public AnalysisSelection GetAnalysisSelection()
    {
        int averagingPeriod = RequireAveragingPeriod(_viewModel.AveragingPeriod);
        int selectedBphIndex = _viewModel.SelectedBphIndex;
        if (selectedBphIndex == 0)
        {
            return new AnalysisSelection(
                averagingPeriod,
                AutoBph: true,
                ManualBph: 0);
        }

        return new AnalysisSelection(
            averagingPeriod,
            AutoBph: false,
            ManualBph: RequireSelectedValue(_manualAutoBph, selectedBphIndex, "manual BPH"));
    }

    public SimulationSelection GetSimulationSelection(IReadOnlyList<int> availableSampleRates, int availableSampleRateCount)
    {
        return new SimulationSelection(
            RequireSelectedValue(_simulationBph, _viewModel.SelectedSimBphIndex, "simulation BPH"),
            GetSelectedSampleRate(availableSampleRates, availableSampleRateCount));
    }

    public int GetSelectedSampleRate(IReadOnlyList<int> availableSampleRates, int availableSampleRateCount)
    {
        return RequireSelectedValue(
            availableSampleRates,
            availableSampleRateCount,
            _viewModel.SelectedSampleRateIndex,
            "sample rate");
    }

    public bool TryGetSelectedSampleRate(
        IReadOnlyList<int> availableSampleRates,
        int availableSampleRateCount,
        out int sampleRate)
    {
        return TryGetSelectedValue(
            availableSampleRates,
            availableSampleRateCount,
            _viewModel.SelectedSampleRateIndex,
            out sampleRate);
    }

    public static int FindValue(IReadOnlyList<int> items, int value)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static int RequireSelectedValue(IReadOnlyList<int> items, int selectedIndex, string selectionName)
    {
        return RequireSelectedValue(items, items.Count, selectedIndex, selectionName);
    }

    private static int RequireSelectedValue(
        IReadOnlyList<int> items,
        int itemCount,
        int selectedIndex,
        string selectionName)
    {
        int boundedCount = Math.Min(itemCount, items.Count);
        if (selectedIndex < 0 || selectedIndex >= boundedCount)
        {
            throw new InvalidOperationException("No valid " + selectionName + " is selected.");
        }

        return items[selectedIndex];
    }

    private static bool TryGetSelectedValue(
        IReadOnlyList<int> items,
        int itemCount,
        int selectedIndex,
        out int value)
    {
        int boundedCount = Math.Min(itemCount, items.Count);
        if (selectedIndex < 0 || selectedIndex >= boundedCount)
        {
            value = 0;
            return false;
        }

        value = items[selectedIndex];
        return true;
    }

    private static int RequireAveragingPeriod(decimal value)
    {
        if (value < MinAveragingPeriodSeconds ||
            value > MaxAveragingPeriodSeconds ||
            decimal.Truncate(value) != value)
        {
            throw new InvalidOperationException("No valid averaging period is selected.");
        }

        return decimal.ToInt32(value);
    }
}
