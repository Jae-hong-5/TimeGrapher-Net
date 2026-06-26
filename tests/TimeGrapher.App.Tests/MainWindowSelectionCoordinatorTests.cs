using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MainWindowSelectionCoordinatorTests
{
    [Fact]
    public void FindText_ContainsMatchIsCaseInsensitive()
    {
        // Preferred-device matching is a substring match, and device-name casing
        // varies by driver (preferred token "CUBILUX CA7" vs a "Cubilux CA7" device),
        // so the substring match must ignore case or auto-select silently fails.
        var items = new[] { "Built-in Audio", "Cubilux CA7 Mono [card 2]" };

        int index = MainWindowSelectionCoordinator.FindText(items, "CUBILUX CA7", matchContains: true);

        Assert.Equal(1, index);
    }

    [Fact]
    public void SelectingPlaybackSourceUsesVirtualDeviceAndDisablesSampleRate()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetInputDeviceNames(new[] { "Live: Mic A", "Playback", "Simulation" });
        var operations = new FakeSelectionOperations(7, -1, -1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        coordinator.SetSelectedInputDeviceIndex(1);

        Assert.Equal(RunCommandMode.Playback, coordinator.CurrentMode);
        Assert.False(vm.IsSampleRateEnabled);
        Assert.False(vm.IsGainEnabled);
        Assert.False(vm.AreSimulationParametersEnabled);
        Assert.Equal(new[] { -1 }, operations.PopulatedDeviceNumbers);
    }

    [Fact]
    public void SelectingSimulationSourceUsesVirtualDeviceAndKeepsSampleRateEnabled()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetInputDeviceNames(new[] { "Live: Mic A", "Playback", "Simulation" });
        var operations = new FakeSelectionOperations(7, -1, -1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        coordinator.SetSelectedInputDeviceIndex(2);

        Assert.Equal(RunCommandMode.Simulation, coordinator.CurrentMode);
        Assert.True(vm.IsSampleRateEnabled);
        Assert.False(vm.IsGainEnabled);
        Assert.True(vm.AreSimulationParametersEnabled);
        Assert.Equal(new[] { -1 }, operations.PopulatedDeviceNumbers);
    }

    [Fact]
    public void SelectingLiveSourceUsesLiveDeviceNumber()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetInputDeviceNames(new[] { "Live: Mic A", "Playback", "Simulation" });
        var operations = new FakeSelectionOperations(7, -1, -1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        coordinator.SetSelectedInputDeviceIndex(0);

        Assert.Equal(RunCommandMode.Live, coordinator.CurrentMode);
        Assert.True(vm.IsSampleRateEnabled);
        Assert.True(vm.IsGainEnabled);
        Assert.False(vm.AreSimulationParametersEnabled);
        Assert.Equal(new[] { 7 }, operations.PopulatedDeviceNumbers);
    }

    [Fact]
    public void SelectingSampleRateUpdatesCurrentSampleRate()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeSelectionOperations(-1)
        {
            AvailableSampleRates = new[] { 48000, 96000 },
        };
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        coordinator.SetSelectedSampleRateIndex(1);

        Assert.Equal(96000, operations.CurrentSampleRate);
    }

    [Fact]
    public void SuppressedSelectionChangesDoNotRunSideEffects()
    {
        MainWindowViewModel vm = CreateViewModel();
        vm.SetInputDeviceNames(new[] { "Live: Mic A", "Playback", "Simulation" });
        var operations = new FakeSelectionOperations(7, -1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        using (coordinator.SuppressEvents())
        {
            vm.SelectedInputDeviceIndex = 1;
        }

        Assert.Empty(operations.PopulatedDeviceNumbers);
    }

    [Fact]
    public void GainChangeUpdatesActiveInputVolume()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeSelectionOperations(-1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        vm.Gain = 250;

        Assert.Equal(0.25f, operations.LastVolume);
    }

    [Fact]
    public void SimulationParameterChangeForwardsLiveValuesWithNegatedBeatError()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeSelectionOperations(-1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        vm.SimErrorRate = 5m;
        vm.SimAmplitude = 250m;
        vm.SimBeatError = 1.5m;

        // Beat error is negated to match SimStart's cfg.BeatErrorMs sign convention;
        // rate, amplitude, and the (default) A/B/C scales pass through unchanged.
        Assert.Equal((5.0, -1.5, 250.0, 1.0, 1.0, 1.0), operations.LastSimulationParameters);
    }

    [Fact]
    public void SignalScaleChangeForwardsClusterScalesLive()
    {
        MainWindowViewModel vm = CreateViewModel();
        var operations = new FakeSelectionOperations(-1);
        MainWindowSelectionCoordinator coordinator = CreateCoordinator(vm, operations);

        vm.SimSignalAScale = 0.3m;
        vm.SimSignalBScale = 1.7m;
        vm.SimSignalCScale = 0.5m;

        Assert.Equal((0.0, 0.0, 300.0, 0.3, 1.7, 0.5), operations.LastSimulationParameters);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel();
    }

    private static MainWindowSelectionCoordinator CreateCoordinator(
        MainWindowViewModel vm,
        FakeSelectionOperations operations)
    {
        var coordinator = new MainWindowSelectionCoordinator(
            vm,
            operations,
            new MainWindowSelectionOptions(
                "Playback",
                "Simulation"));
        vm.PropertyChanged += coordinator.OnViewModelPropertyChanged;
        return coordinator;
    }

    private sealed class FakeSelectionOperations : IMainWindowSelectionOperations
    {
        public FakeSelectionOperations(params int[] inputDeviceNumbers)
        {
            InputDeviceNumbers = inputDeviceNumbers;
        }

        public IReadOnlyList<int> InputDeviceNumbers { get; }

        public int[] AvailableSampleRates { get; set; } = Array.Empty<int>();

        public int AvailableSampleRateCount => AvailableSampleRates.Length;

        public List<int> PopulatedDeviceNumbers { get; } = new();

        public int CurrentSampleRate { get; private set; }

        public float? LastVolume { get; private set; }

        public (double RateErrorSPerDay, double BeatErrorMs, double WatchAmplitudeDegrees, double AClusterLevelScale, double BClusterLevelScale, double CClusterLevelScale)? LastSimulationParameters { get; private set; }

        public int GetAvailableSampleRate(int index)
        {
            return AvailableSampleRates[index];
        }

        public void PopulateSampleRates(int deviceNumber)
        {
            PopulatedDeviceNumbers.Add(deviceNumber);
        }

        public void SetCurrentSampleRate(int sampleRate)
        {
            CurrentSampleRate = sampleRate;
        }

        public void SetAudioInputVolume(float normalizedVolume)
        {
            LastVolume = normalizedVolume;
        }

        public void SetLiveSimulationParameters(
            double rateErrorSPerDay,
            double beatErrorMs,
            double watchAmplitudeDegrees,
            double aClusterLevelScale,
            double bClusterLevelScale,
            double cClusterLevelScale)
        {
            LastSimulationParameters = (rateErrorSPerDay, beatErrorMs, watchAmplitudeDegrees, aClusterLevelScale, bClusterLevelScale, cClusterLevelScale);
        }

    }
}
