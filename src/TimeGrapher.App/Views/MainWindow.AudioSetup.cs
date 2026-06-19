using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;
using TimeGrapher.App.Audio;
using TimeGrapher.App.Services;
using TimeGrapher.Core.Detection;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private void ConfigureSoundCard()
    {
        LiveAudioBackend.ConfigurePreferredInput();
    }

    private void LoadAudioDevices(string? currentDeviceName = null)
    {
        IReadOnlyList<LiveAudioDevice> inputDevices = Array.Empty<LiveAudioDevice>();
        if (LiveAudioBackend.CanCapture)
        {
            try
            {
                inputDevices = LiveAudioBackend.EnumerateInputDevices();
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
        mInputDeviceNumbers.Clear();
        // Fresh cache instance per enumeration: a still-running pre-warm from a
        // previous enumeration keeps writing to its own (now-orphaned) instance, so
        // it can't pollute the new cache with stale rates for a reused device number.
        var probeCache = new ConcurrentDictionary<int, IReadOnlyList<int>>();
        _sampleRateProbeCache = probeCache;

        int renameLen = RenameAudioDevices.Length;
        for (int dev = 0; dev < inputDevices.Count; dev++)
        {
            LiveAudioDevice device = inputDevices[dev];
            string description = device.Name;
            for (int i = 0; i < renameLen; i++)
            {
                if (description.Contains(RenameAudioDevices[i][0], StringComparison.Ordinal))
                {
                    description = RenameAudioDevices[i][1];
                    break;
                }
            }

            deviceNames.Add("Live: " + description);
            mInputDeviceNumbers.Add(device.Number);
        }

        deviceNames.Add(PLAYBACK_SOURCE);
        mInputDeviceNumbers.Add(-1);
        deviceNames.Add(SIMULATION_SOURCE);
        mInputDeviceNumbers.Add(-1);

        // Pre-warm the per-device probe cache off the UI thread so a later device
        // selection (and the rate restore after a Playback/Sim run) reads cached
        // rates instantly instead of running the per-rate probe on the UI thread.
        int[] liveDeviceNumbers = mInputDeviceNumbers.Where(number => number >= 0).Distinct().ToArray();
        if (liveDeviceNumbers.Length > 0)
        {
            Task.Run(() =>
            {
                foreach (int number in liveDeviceNumbers)
                {
                    // Warm the instance captured for this enumeration, not the live
                    // field, so a later re-enumeration's swap orphans this task.
                    probeCache.GetOrAdd(number, LiveAudioBackend.GetCandidateSampleRates);
                }
            });
        }

        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetInputDeviceNames(deviceNames);
        }

        int selected = SelectInputDeviceIndexAfterReload(mViewModel.InputDeviceNames, currentDeviceName);

        // setCurrentIndex(index) triggers on_InputDeviceComboBox_currentIndexChanged once.
        // (Avalonia ComboBox does not auto-select on add, unlike Qt; explicitly select to
        //  reach the same final state where PopulateSampleRates has run for the chosen device.)
        if (selected != -1)
        {
            mSelectionCoordinator.SetSelectedInputDeviceIndex(selected, forceChanged: true);
        }
        else if (mViewModel.InputDeviceNames.Count > 0)
        {
            // No preferred device matched: fall back to index 0 (Qt's auto-selected first item).
            if (mViewModel.SelectedInputDeviceIndex == 0)
            {
                mSelectionCoordinator.SetSelectedInputDeviceIndex(0, forceChanged: true); // re-run logic; index unchanged
            }
            else
            {
                mSelectionCoordinator.SetSelectedInputDeviceIndex(0);
            }
        }
    }

    private void OnInputDeviceComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        LoadAudioDevices(CurrentInputDeviceText());
    }

    internal static int SelectInputDeviceIndexAfterReload(
        IReadOnlyList<string> deviceNames,
        string? currentDeviceName)
    {
        if (!string.IsNullOrEmpty(currentDeviceName))
        {
            int currentIndex = MainWindowSelectionCoordinator.FindText(
                deviceNames,
                currentDeviceName,
                matchContains: false);
            if (currentIndex != -1)
            {
                return currentIndex;
            }
        }

        int len = PreferredAudioDevices.Length;
        for (int i = 0; i < len; i++)
        {
            int index = MainWindowSelectionCoordinator.FindText(
                deviceNames,
                PreferredAudioDevices[i],
                matchContains: true);
            if (index != -1) // -1 means the text was not found
            {
                return index;
            }
        }

        return -1;
    }

    private void LoadAveragingPeriod()
    {
        List<string> labels = BuildAveragingPeriodLabels();
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetAveragingPeriodLabels(labels);
            mViewModel.SelectedAveragingPeriodIndex = -1;
        }

        int defaultIndex = mRunSelectionResolver.DefaultAveragingPeriodIndex;
        mViewModel.SelectedAveragingPeriodIndex = defaultIndex == -1 ? 0 : defaultIndex;
    }

    private void LoadBph()
    {
        List<string> labels = BuildBphLabels(BphCatalog.ManualAutoBph, useAutoLabel: true);
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetBphLabels(labels);
            mViewModel.SelectedBphIndex = -1;
        }

        mViewModel.SelectedBphIndex = 0; // Auto
    }

    private void LoadSimBph()
    {
        List<string> labels = BuildBphLabels(BphCatalog.ManualBph, useAutoLabel: false);
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetSimBphLabels(labels);
            mViewModel.SelectedSimBphIndex = -1;
        }

        int defaultIndex = mRunSelectionResolver.DefaultSimulationBphIndex;
        mViewModel.SelectedSimBphIndex = defaultIndex == -1 ? 0 : defaultIndex;
    }

    private static List<string> BuildAveragingPeriodLabels()
    {
        int length = AveragingPeriodList.Length;
        var labels = new List<string>(length);
        for (int i = 0; i < length; i++)
        {
            labels.Add(AveragingPeriodList[i].ToString(CultureInfo.InvariantCulture) + "s");
        }

        return labels;
    }

    private static List<string> BuildBphLabels(IReadOnlyList<int> bphValues, bool useAutoLabel)
    {
        var labels = new List<string>(bphValues.Count);
        for (int i = 0; i < bphValues.Count; i++)
        {
            int bph = bphValues[i];
            string name = useAutoLabel && bph == 0
                ? "Auto BPH"
                : bph.ToString(CultureInfo.InvariantCulture);
            labels.Add(name);
        }

        return labels;
    }

    // Per-device probe cache: the supported-rate probe is stable for a device
    // during a session and (on Linux) spawns a process per rate, so cache it and
    // pre-warm it off the UI thread (see LoadAudioDevices). A warm read narrows
    // the list synchronously; a cache miss (e.g. the initial auto-select racing
    // the pre-warm) fills all rates and probes off the UI thread, so the probe
    // never freezes the UI thread.
    private ConcurrentDictionary<int, IReadOnlyList<int>> _sampleRateProbeCache = new();

    // Probe a device's supported rates off the UI thread, then re-narrow the list
    // on the UI thread if that device is still selected (a stale probe is dropped).
    private void ProbeSampleRatesAsync(int deviceNumber)
    {
        // Capture the current cache instance: a device re-enumeration swaps the
        // field for a fresh cache, so write the probe into the cache this call
        // belongs to (a later swap orphans it) and re-narrow only if that same
        // cache is still current and the same device is still selected.
        ConcurrentDictionary<int, IReadOnlyList<int>> cache = _sampleRateProbeCache;
        Task.Run(() =>
        {
            cache.GetOrAdd(deviceNumber, LiveAudioBackend.GetCandidateSampleRates);
            Dispatcher.UIThread.Post(() =>
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
        int index = mViewModel.SelectedInputDeviceIndex;
        return index >= 0 && index < mInputDeviceNumbers.Count
            ? mInputDeviceNumbers[index]
            : int.MinValue;
    }

    private void PopulateSampleRates(int deviceNumber)
    {
        IReadOnlyList<int> standardRates = AudioSampleRates.Standard;

        mNumberOfRates = 0;
        var labels = new List<string>(standardRates.Count);

        if (deviceNumber < 0)
        {
            foreach (int rate in standardRates)
            {
                labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                mAvailableRates[mNumberOfRates] = rate;
                mNumberOfRates++;
            }
        }
        else if (_sampleRateProbeCache.TryGetValue(deviceNumber, out IReadOnlyList<int>? supported))
        {
            // Warm cache: narrow to the device-supported rates instantly. Capture
            // backend startup remains the authoritative validation.
            foreach (int rate in standardRates)
            {
                if (supported.Contains(rate) && mNumberOfRates < mAvailableRates.Length)
                {
                    labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                    mAvailableRates[mNumberOfRates] = rate;
                    mNumberOfRates++;
                }
            }
        }
        else
        {
            // Cache miss (pre-warm not finished, e.g. the initial auto-select):
            // show every standard rate now so the UI never blocks on the probe
            // (on Linux it spawns a process per rate), then probe off the UI
            // thread and re-narrow once it returns if the device is still chosen.
            foreach (int rate in standardRates)
            {
                if (mNumberOfRates < mAvailableRates.Length)
                {
                    labels.Add(rate.ToString(CultureInfo.InvariantCulture) + " Hz");
                    mAvailableRates[mNumberOfRates] = rate;
                    mNumberOfRates++;
                }
            }

            ProbeSampleRatesAsync(deviceNumber);
        }

        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetSampleRateLabels(labels);
            mViewModel.SelectedSampleRateIndex = -1;
        }

        if (mViewModel.SampleRateLabels.Count > 0)
        {
            mSelectionCoordinator.SetSelectedSampleRateIndex(0);
        }
    }

    private bool SetAudioRate(int rate)
    {
        return mSelectionCoordinator.SetAudioRate(rate);
    }

    private bool SetAudioDevice(string name)
    {
        return mSelectionCoordinator.SetAudioDevice(name);
    }

    private void RestorePlaybackOrSimulationAudioState()
    {
        SetAudioDevice(mDeviceNameBeforePlaybackOrSim);
        SetAudioRate(mRateBeforePlaybackOrSim);
    }

    private void GetAudioRate(out int rate)
    {
        rate = mCurrentSamplesPerSecond;
    }

    private void GetAudioDevice(out string name)
    {
        name = CurrentInputDeviceText();
    }

    private int CurrentInputDeviceNumber()
    {
        return mSelectionCoordinator.CurrentInputDeviceNumber;
    }

    private string CurrentInputDeviceText()
    {
        return mSelectionCoordinator.CurrentInputDeviceText;
    }

    private RunCommandMode CurrentMode()
    {
        return mSelectionCoordinator.CurrentMode;
    }

}
