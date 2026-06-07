using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

    private void LoadAudioDevices()
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
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetInputDeviceNames(deviceNames);
        }

        int len = PreferredAudioDevices.Length;
        int selected = -1;
        for (int i = 0; i < len; i++)
        {
            int index = MainWindowSelectionCoordinator.FindText(
                mViewModel.InputDeviceNames,
                PreferredAudioDevices[i],
                matchContains: true);
            if (index != -1) // -1 means the text was not found
            {
                selected = index;
                break;
            }
        }

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
        else
        {
            IReadOnlyList<int> supported = LiveAudioBackend.GetCandidateSampleRates(deviceNumber);
            // Capture backend startup remains the authoritative validation point.
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
