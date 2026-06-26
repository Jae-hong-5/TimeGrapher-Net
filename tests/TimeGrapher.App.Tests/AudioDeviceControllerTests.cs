using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TimeGrapher.App.Services;
using TimeGrapher.App.ViewModels;
using TimeGrapher.App.Views;
using TimeGrapher.Core.Shared;
using Xunit;

namespace TimeGrapher.App.Tests;

/// <summary>
/// AudioDeviceController owns the device-enumeration / rate-probe flow lifted out of the MainWindow.
/// With fake backend/dispatcher/gate seams these lock the deterministic behavior: enumeration +
/// rename + Playback/Simulation append, label building for the negative/warm/miss branches, and the
/// stale-probe rejection (a probe whose enumeration was superseded must not re-narrow the new list).
/// </summary>
public sealed class AudioDeviceControllerTests
{
    private sealed class FakeBackend : IAudioDeviceBackend
    {
        public bool CanCapture { get; set; } = true;
        public bool ThrowOnEnumerate { get; set; }
        public List<LiveAudioDevice> Devices { get; } = new();
        public Dictionary<int, IReadOnlyList<int>> RatesByDevice { get; } = new();

        public IReadOnlyList<LiveAudioDevice> EnumerateInputDevices() =>
            ThrowOnEnumerate ? throw new InvalidOperationException("enumeration failed") : Devices;

        public IReadOnlyList<int> GetCandidateSampleRates(int deviceNumber) =>
            RatesByDevice.TryGetValue(deviceNumber, out IReadOnlyList<int>? rates) ? rates : Array.Empty<int>();
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeGate : ISelectionEventGate
    {
        private sealed class Noop : IDisposable { public void Dispose() { } }

        public List<(int Index, bool ForceChanged)> SelectedInputDeviceCalls { get; } = new();
        public List<int> SelectedSampleRateIndices { get; } = new();

        public IDisposable SuppressEvents() => new Noop();
        public void SetSelectedInputDeviceIndex(int index, bool forceChanged = false) => SelectedInputDeviceCalls.Add((index, forceChanged));
        public void SetSelectedSampleRateIndex(int index, bool forceChanged = false) => SelectedSampleRateIndices.Add(index);
    }

    private sealed class ControllableRunner
    {
        public List<Action> Pending { get; } = new();
        public bool Immediate { get; set; } = true;

        public Task Run(Action action)
        {
            if (Immediate)
            {
                action();
            }
            else
            {
                Pending.Add(action);
            }

            return Task.CompletedTask;
        }

        public void RunPending()
        {
            List<Action> copy = Pending.ToList();
            Pending.Clear();
            foreach (Action action in copy)
            {
                action();
            }
        }
    }

    private sealed class Harness
    {
        public MainWindowViewModel ViewModel { get; } = new();
        public AudioSelectionState State { get; } = new();
        public FakeBackend Backend { get; } = new();
        public FakeGate Gate { get; } = new();
        public ControllableRunner Runner { get; } = new();
        public AudioDeviceController Controller { get; }

        public Harness(
            Func<string, string>? rename = null,
            Func<IReadOnlyList<string>, string?, int>? selector = null,
            AudioSelectionPreference preference = default)
        {
            Controller = new AudioDeviceController(
                ViewModel,
                State,
                Backend,
                new ImmediateDispatcher(),
                rename ?? (name => name),
                selectInputDeviceIndexAfterReload: selector ?? ((_, _) => 0),
                playbackSourceName: "Playback",
                simulationSourceName: "Simulation",
                preference: preference,
                runOffThread: Runner.Run);
            Controller.AttachSelectionEventGate(Gate);
        }
    }

    private sealed class CoordinatedHarness
    {
        public MainWindowViewModel ViewModel { get; } = new();
        public AudioSelectionState State { get; } = new();
        public FakeBackend Backend { get; } = new();
        public ControllableRunner Runner { get; } = new();
        public MainWindowSelectionCoordinator Coordinator { get; }
        public AudioDeviceController Controller { get; }

        public CoordinatedHarness()
        {
            var operations = new CoordinatorOperations(State);
            Coordinator = new MainWindowSelectionCoordinator(
                ViewModel,
                operations,
                new MainWindowSelectionOptions("Playback", "Simulation"));
            Controller = new AudioDeviceController(
                ViewModel,
                State,
                Backend,
                new ImmediateDispatcher(),
                name => name,
                MainWindow.SelectInputDeviceIndexAfterReload,
                playbackSourceName: "Playback",
                simulationSourceName: "Simulation",
                runOffThread: Runner.Run);
            operations.PopulateSampleRatesAction = Controller.PopulateSampleRates;
            Controller.AttachSelectionEventGate(Coordinator);
            ViewModel.PropertyChanged += Coordinator.OnViewModelPropertyChanged;
        }
    }

    private sealed class CoordinatorOperations : IMainWindowSelectionOperations
    {
        private readonly AudioSelectionState _state;

        public CoordinatorOperations(AudioSelectionState state)
        {
            _state = state;
        }

        public IReadOnlyList<int> InputDeviceNumbers => _state.InputDeviceNumbers;

        public int AvailableSampleRateCount => _state.AvailableSampleRateCount;

        public Action<int> PopulateSampleRatesAction { get; set; } = _ => { };

        public int GetAvailableSampleRate(int index) => _state.GetAvailableSampleRate(index);

        public void PopulateSampleRates(int deviceNumber) => PopulateSampleRatesAction(deviceNumber);

        public void SetCurrentSampleRate(int sampleRate) => _state.CurrentSampleRate = sampleRate;

        public void SetAudioInputVolume(float normalizedVolume)
        {
            _ = normalizedVolume;
        }

        public void SetLiveSimulationParameters(double rateErrorSPerDay, double beatErrorMs, double watchAmplitudeDegrees)
        {
            _ = rateErrorSPerDay;
            _ = beatErrorMs;
            _ = watchAmplitudeDegrees;
        }
    }

    [Fact]
    public void LoadAudioDevices_CanCaptureFalse_OnlyPlaybackAndSimulation()
    {
        var h = new Harness();
        h.Backend.CanCapture = false;

        h.Controller.LoadAudioDevices();

        Assert.Equal(new[] { -1, -1 }, h.State.InputDeviceNumbers);
        Assert.Equal(new[] { "Playback", "Simulation" }, h.ViewModel.InputDeviceNames);
        // The chosen index must be applied with forceChanged: true so the selection logic re-runs.
        Assert.Equal(new[] { (0, true) }, h.Gate.SelectedInputDeviceCalls);
    }

    [Fact]
    public void LoadAudioDevices_EnumeratesRenamesAndAppendsSources()
    {
        var h = new Harness(rename: name => "R(" + name + ")");
        h.Backend.Devices.Add(new LiveAudioDevice(7, "Welshi"));
        h.Backend.Devices.Add(new LiveAudioDevice(9, "Other"));

        h.Controller.LoadAudioDevices();

        Assert.Equal(new[] { 7, 9, -1, -1 }, h.State.InputDeviceNumbers);
        Assert.Equal(
            new[] { "Live: R(Welshi)", "Live: R(Other)", "Playback", "Simulation" },
            h.ViewModel.InputDeviceNames);
    }

    [Fact]
    public void LoadAudioDevices_EnumerationThrows_FallsBackToPlaybackSimulation()
    {
        var h = new Harness();
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));
        h.Backend.ThrowOnEnumerate = true; // backend enumeration fails

        h.Controller.LoadAudioDevices(); // the catch swallows it; only the synthetic sources remain

        Assert.Equal(new[] { -1, -1 }, h.State.InputDeviceNumbers);
        Assert.Equal(new[] { "Playback", "Simulation" }, h.ViewModel.InputDeviceNames);
    }

    [Fact]
    public void LoadAudioDevices_NoPreferredMatch_SelectsIndexZeroWithoutForce()
    {
        var h = new Harness(selector: (_, _) => -1); // no preferred device matched
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));

        h.Controller.LoadAudioDevices();

        // Fallback to index 0; the initial selected index is -1 (not 0), so forceChanged stays false.
        Assert.Equal(new[] { (0, false) }, h.Gate.SelectedInputDeviceCalls);
    }

    [Fact]
    public void LoadAudioDevices_InitialLoadUsesPersistedDevicePreference()
    {
        var h = new Harness(
            selector: MainWindow.SelectInputDeviceIndexAfterReload,
            preference: new AudioSelectionPreference("Playback", 48000));
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));

        h.Controller.LoadAudioDevices();

        Assert.Equal(new[] { (1, true) }, h.Gate.SelectedInputDeviceCalls);
    }

    [Theory]
    [InlineData("Playback", 2, (int)RunCommandMode.Playback)]
    [InlineData("Simulation", 3, (int)RunCommandMode.Simulation)]
    [InlineData("Live: Mic B", 1, (int)RunCommandMode.Live)]
    public void LoadAudioDevices_PreservesCurrentSourceThroughSelectionCoordinator(
        string currentDeviceName,
        int expectedIndex,
        int expectedMode)
    {
        var h = new CoordinatedHarness();
        h.Backend.Devices.Add(new LiveAudioDevice(7, "Mic A"));
        h.Backend.Devices.Add(new LiveAudioDevice(9, "Mic B"));

        h.Controller.LoadAudioDevices(currentDeviceName);

        Assert.Equal(expectedIndex, h.ViewModel.SelectedInputDeviceIndex);
        Assert.Equal(
            currentDeviceName,
            MainWindowSelectionCoordinator.ItemText(
                h.ViewModel.InputDeviceNames,
                h.ViewModel.SelectedInputDeviceIndex));
        Assert.Equal((RunCommandMode)expectedMode, h.Coordinator.CurrentMode);
    }

    [Fact]
    public void PopulateSampleRates_NegativeDevice_ShowsAllStandardRates()
    {
        var h = new Harness();

        h.Controller.PopulateSampleRates(-1);

        Assert.Equal(new[] { "48000 Hz", "96000 Hz", "192000 Hz" }, h.ViewModel.SampleRateLabels);
        Assert.Equal(3, h.State.AvailableSampleRateCount);
        Assert.Equal(new[] { 0 }, h.Gate.SelectedSampleRateIndices);
    }

    [Fact]
    public void PopulateSampleRates_SelectsPersistedSampleRateWhenAvailable()
    {
        var h = new Harness(preference: new AudioSelectionPreference("Playback", 96000));

        h.Controller.PopulateSampleRates(-1);

        Assert.Equal(new[] { 1 }, h.Gate.SelectedSampleRateIndices);
        Assert.Equal(96000, h.State.CurrentSampleRate);
    }

    [Fact]
    public void PopulateSampleRates_WarmCache_IntersectsSupportedRates()
    {
        var h = new Harness();
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));
        h.Backend.RatesByDevice[1] = new[] { 48000 }; // 96000/192000 unsupported
        h.Controller.LoadAudioDevices(); // immediate runner -> pre-warms the cache for device 1

        h.Controller.PopulateSampleRates(1);

        Assert.Equal(new[] { "48000 Hz" }, h.ViewModel.SampleRateLabels);
    }

    [Fact]
    public void ProbeSampleRates_StaleAfterReEnumeration_IsIgnored()
    {
        var h = new Harness();
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));
        h.Backend.RatesByDevice[1] = new[] { 48000 };
        h.Runner.Immediate = false; // defer the pre-warm and the probe so we can interleave

        h.Controller.LoadAudioDevices();      // pre-warm deferred -> cache stays empty
        h.ViewModel.SelectedInputDeviceIndex = 0; // device 1 is selected
        h.Controller.PopulateSampleRates(1);  // cache miss -> all standard now + probe deferred

        Assert.Equal(3, h.ViewModel.SampleRateLabels.Count); // miss branch shows all standard

        h.Controller.LoadAudioDevices();      // re-enumerate -> swaps the cache instance

        h.Runner.RunPending(); // runs the stale probe (old cache) + the pre-warms

        // The stale probe's re-narrow is dropped (cache identity changed), so the list is not
        // narrowed to the single supported rate.
        Assert.Equal(3, h.ViewModel.SampleRateLabels.Count);
    }

    [Fact]
    public void ProbeSampleRates_AfterSelectionChange_IsIgnored()
    {
        var h = new Harness();
        h.Backend.Devices.Add(new LiveAudioDevice(1, "Mic"));
        h.Backend.RatesByDevice[1] = new[] { 48000 };
        h.Runner.Immediate = false; // defer the pre-warm and the probe

        h.Controller.LoadAudioDevices();          // state: device 1, Playback, Simulation
        h.ViewModel.SelectedInputDeviceIndex = 0; // device 1 selected
        h.Controller.PopulateSampleRates(1);      // cache miss -> all standard now + probe deferred

        Assert.Equal(3, h.ViewModel.SampleRateLabels.Count);

        // Move selection to Playback (-1) WITHOUT re-enumerating, so the cache identity is unchanged
        // but the device-match guard (CurrentSelectedInputDeviceNumber == deviceNumber) now fails.
        h.ViewModel.SelectedInputDeviceIndex = 1;

        h.Runner.RunPending(); // pre-warm fills the cache; the device-1 probe must NOT re-narrow

        Assert.Equal(3, h.ViewModel.SampleRateLabels.Count);
    }
}
