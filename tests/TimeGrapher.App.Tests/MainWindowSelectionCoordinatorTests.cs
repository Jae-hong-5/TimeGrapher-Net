using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class MainWindowSelectionCoordinatorTests
{
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

    }
}
