using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TimeGrapher.App.ViewModels;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// Owns audio input-device enumeration and per-device sample-rate probing — the async,
/// UI-marshalling flow the MainWindow code-behind used to hold. Enumerates devices (renamed for
/// display), fills the device/rate combos via the view-model, pre-warms a per-device rate cache off
/// the UI thread, and on a cache miss probes off-thread then re-narrows on the UI thread (dropping a
/// stale probe whose enumeration was superseded). The selection-event gate is attached after
/// construction (it is the coordinator that drives this controller, so the dependency is cyclic).
/// </summary>
internal sealed class AudioDeviceController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AudioSelectionState _state;
    private readonly IAudioDeviceBackend _backend;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<Action, Task> _runOffThread;
    private readonly Func<string, string> _renameDeviceName;
    private readonly Func<IReadOnlyList<string>, string?, int> _selectInputDeviceIndexAfterReload;
    private readonly string _playbackSourceName;
    private readonly string _simulationSourceName;

    // Attached after construction (the coordinator that calls this controller). Never null in use.
    private ISelectionEventGate _gate = null!;

    // Per-device probe cache, swapped for a fresh instance on each enumeration so a still-running
    // pre-warm from a previous enumeration orphans itself (it keeps writing its own instance).
    private ConcurrentDictionary<int, IReadOnlyList<int>> _sampleRateProbeCache = new();

    public AudioDeviceController(
        MainWindowViewModel viewModel,
        AudioSelectionState state,
        IAudioDeviceBackend backend,
        IUiDispatcher dispatcher,
        Func<string, string> renameDeviceName,
        Func<IReadOnlyList<string>, string?, int> selectInputDeviceIndexAfterReload,
        string playbackSourceName,
        string simulationSourceName,
        Func<Action, Task>? runOffThread = null)
    {
        _viewModel = viewModel;
        _state = state;
        _backend = backend;
        _dispatcher = dispatcher;
        _renameDeviceName = renameDeviceName;
        _selectInputDeviceIndexAfterReload = selectInputDeviceIndexAfterReload;
        _playbackSourceName = playbackSourceName;
        _simulationSourceName = simulationSourceName;
        _runOffThread = runOffThread ?? (action => Task.Run(action));
    }

    /// <summary>Attaches the selection-event gate (the coordinator) once it has been constructed.</summary>
    public void AttachSelectionEventGate(ISelectionEventGate gate) => _gate = gate;

    public void LoadAudioDevices(string? currentDeviceName = null)
    {
        IReadOnlyList<LiveAudioDevice> inputDevices = Array.Empty<LiveAudioDevice>();
        if (_backend.CanCapture)
        {
            try
            {
                inputDevices = _backend.EnumerateInputDevices();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Live audio device enumeration failed: " + ex.Message);
            }
        }
        else
        {
            Console.Error.WriteLine("Live audio capture is not available on this platform; using Playback/Simulation only.");
        }

        var deviceNames = new List<string>();
        _state.ClearInputDevices();
        // Fresh cache instance per enumeration: a still-running pre-warm from a previous
        // enumeration keeps writing its own (now-orphaned) instance, so it can't pollute the new
        // cache with stale rates for a reused device number.
        var probeCache = new ConcurrentDictionary<int, IReadOnlyList<int>>();
        _sampleRateProbeCache = probeCache;

        foreach (LiveAudioDevice device in inputDevices)
        {
            deviceNames.Add("Live: " + _renameDeviceName(device.Name));
            _state.AddInputDevice(device.Number);
        }

        deviceNames.Add(_playbackSourceName);
        _state.AddInputDevice(-1);
        deviceNames.Add(_simulationSourceName);
        _state.AddInputDevice(-1);

        // Pre-warm the per-device probe cache off the UI thread so a later device selection (and the
        // rate restore after a Playback/Sim run) reads cached rates instantly instead of running the
        // per-rate probe on the UI thread.
        int[] liveDeviceNumbers = _state.InputDeviceNumbers.Where(number => number >= 0).Distinct().ToArray();
        if (liveDeviceNumbers.Length > 0)
        {
            _runOffThread(() =>
            {
                foreach (int number in liveDeviceNumbers)
                {
                    // Warm the instance captured for this enumeration, not the live field, so a
                    // later re-enumeration's swap orphans this task.
                    probeCache.GetOrAdd(number, _backend.GetCandidateSampleRates);
                }
            });
        }

        using (_gate.SuppressEvents())
        {
            _viewModel.SetInputDeviceNames(deviceNames);
        }

        int selected = _selectInputDeviceIndexAfterReload(_viewModel.InputDeviceNames, currentDeviceName);

        // Avalonia ComboBox does not auto-select on add (unlike Qt); explicitly select so
        // PopulateSampleRates runs for the chosen device, reaching the same final state.
        if (selected != -1)
        {
            _gate.SetSelectedInputDeviceIndex(selected, forceChanged: true);
        }
        else if (_viewModel.InputDeviceNames.Count > 0)
        {
            // No preferred device matched: fall back to index 0 (Qt's auto-selected first item).
            if (_viewModel.SelectedInputDeviceIndex == 0)
            {
                _gate.SetSelectedInputDeviceIndex(0, forceChanged: true); // re-run logic; index unchanged
            }
            else
            {
                _gate.SetSelectedInputDeviceIndex(0);
            }
        }
    }

    public void PopulateSampleRates(int deviceNumber)
    {
        IReadOnlyList<int> standardRates = AudioSampleRates.Standard;

        _state.ResetSampleRates();
        var labels = new List<string>(standardRates.Count);

        if (deviceNumber < 0)
        {
            foreach (int rate in standardRates)
            {
                if (_state.TryAddSampleRate(rate))
                {
                    labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                }
            }
        }
        else if (_sampleRateProbeCache.TryGetValue(deviceNumber, out IReadOnlyList<int>? supported))
        {
            // Warm cache: narrow to the device-supported rates instantly. Capture backend startup
            // remains the authoritative validation.
            foreach (int rate in standardRates)
            {
                if (supported.Contains(rate) && _state.TryAddSampleRate(rate))
                {
                    labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                }
            }
        }
        else
        {
            // Cache miss (pre-warm not finished, e.g. the initial auto-select): show every standard
            // rate now so the UI never blocks on the probe (on Linux it spawns a process per rate),
            // then probe off the UI thread and re-narrow once it returns if the device is still chosen.
            foreach (int rate in standardRates)
            {
                if (_state.TryAddSampleRate(rate))
                {
                    labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                }
            }

            ProbeSampleRatesAsync(deviceNumber);
        }

        using (_gate.SuppressEvents())
        {
            _viewModel.SetSampleRateLabels(labels);
            _viewModel.SelectedSampleRateIndex = -1;
        }

        if (_viewModel.SampleRateLabels.Count > 0)
        {
            _gate.SetSelectedSampleRateIndex(0);
        }
    }

    // Probe a device's supported rates off the UI thread, then re-narrow the list on the UI thread
    // if that device is still selected (a stale probe from a superseded enumeration is dropped).
    private void ProbeSampleRatesAsync(int deviceNumber)
    {
        // Capture the current cache instance: a device re-enumeration swaps the field for a fresh
        // cache, so write the probe into the cache this call belongs to (a later swap orphans it)
        // and re-narrow only if that same cache is still current and the same device is still selected.
        ConcurrentDictionary<int, IReadOnlyList<int>> cache = _sampleRateProbeCache;
        _runOffThread(() =>
        {
            cache.GetOrAdd(deviceNumber, _backend.GetCandidateSampleRates);
            _dispatcher.Post(() =>
            {
                if (ReferenceEquals(_sampleRateProbeCache, cache) &&
                    CurrentSelectedInputDeviceNumber() == deviceNumber)
                {
                    // The cache is now warm, so this re-narrows synchronously.
                    PopulateSampleRates(deviceNumber);
                }
            });
        });
    }

    private int CurrentSelectedInputDeviceNumber()
    {
        int index = _viewModel.SelectedInputDeviceIndex;
        return index >= 0 && index < _state.InputDeviceNumbers.Count
            ? _state.InputDeviceNumbers[index]
            : int.MinValue;
    }
}
