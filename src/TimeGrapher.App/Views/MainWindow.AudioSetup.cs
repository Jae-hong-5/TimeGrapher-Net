using System;
using System.Collections.Generic;
using System.Globalization;

using TimeGrapher.App.Audio;
using TimeGrapher.App.Services;
using TimeGrapher.Core.Detection;

namespace TimeGrapher.App.Views;

public partial class MainWindow
{
    private void ConfigureSoundCard()
    {
        LiveAudioBackend.ConfigurePreferredInput();
    }

    // The device-name rename rules the AudioDeviceController applies for display
    // (substring match -> preferred name; otherwise the device's own name).
    internal static string RenameDeviceName(string deviceName)
    {
        foreach (string[] rename in RenameAudioDevices)
        {
            if (deviceName.Contains(rename[0], StringComparison.Ordinal))
            {
                return rename[1];
            }
        }

        return deviceName;
    }

    private void OnInputDeviceComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        mAudioDeviceController.LoadAudioDevices(CurrentInputDeviceText());
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

    private void LoadBph()
    {
        List<string> labels = BuildBphLabels(BphCatalog.ManualAutoBph, useAutoLabel: true);
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetBphLabels(labels);
            mViewModel.SelectedBphIndex = -1;
        }

        int savedIndex = RunSelectionResolver.FindValue(BphCatalog.ManualAutoBph, AppSettings.Current.LeftPanel.Bph);
        mViewModel.SelectedBphIndex = savedIndex == -1 ? 0 : savedIndex;
    }

    private void LoadSimBph()
    {
        List<string> labels = BuildBphLabels(BphCatalog.ManualBph, useAutoLabel: false);
        using (mSelectionCoordinator.SuppressEvents())
        {
            mViewModel.SetSimBphLabels(labels);
            mViewModel.SelectedSimBphIndex = -1;
        }

        int savedIndex = RunSelectionResolver.FindValue(BphCatalog.ManualBph, AppSettings.Current.LeftPanel.SimulationBph);
        if (savedIndex == -1)
        {
            savedIndex = mRunSelectionResolver.DefaultSimulationBphIndex;
        }

        mViewModel.SelectedSimBphIndex = savedIndex == -1 ? 0 : savedIndex;
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
        rate = mAudioSelection.CurrentSampleRate;
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
