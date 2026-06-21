using System.Collections.Generic;

namespace TimeGrapher.App.Services;

/// <summary>
/// The audio input-device / sample-rate selection state the MainWindow held as loose fields: the
/// input-device numbers parallel to the device combo, the probed sample-rate buffer with its valid
/// count, and the current sample rate. Lifted into a non-UI holder so the selection operations and
/// run settings read it instead of reaching into the View. UI-thread-confined, like the fields it
/// replaces (the async rate probe writes a separate cache, not this state).
/// </summary>
internal sealed class AudioSelectionState
{
    // Fixed sample-rate buffer; PopulateSampleRates fills the first AvailableSampleRateCount
    // entries (mirrors the original int[5] + mNumberOfRates pair, including its cap).
    private readonly int[] _availableSampleRates = new int[5];
    private readonly List<int> _inputDeviceNumbers = new();

    /// <summary>The sample rate the current/next run uses (defaults to the app's startup rate).</summary>
    public int CurrentSampleRate { get; set; } = 48000;

    /// <summary>Device numbers parallel to the input-device combo entries (-1 for Playback/Simulation).</summary>
    public IReadOnlyList<int> InputDeviceNumbers => _inputDeviceNumbers;

    public int AvailableSampleRateCount { get; private set; }

    /// <summary>The probed-rate buffer; only the first <see cref="AvailableSampleRateCount"/> entries are valid.</summary>
    public IReadOnlyList<int> AvailableSampleRates => _availableSampleRates;

    public int GetAvailableSampleRate(int index) => _availableSampleRates[index];

    public void ClearInputDevices() => _inputDeviceNumbers.Clear();

    public void AddInputDevice(int deviceNumber) => _inputDeviceNumbers.Add(deviceNumber);

    public void ResetSampleRates() => AvailableSampleRateCount = 0;

    /// <summary>Appends a probed rate if the buffer has room, returning whether it was added.</summary>
    public bool TryAddSampleRate(int sampleRate)
    {
        if (AvailableSampleRateCount >= _availableSampleRates.Length)
        {
            return false;
        }

        _availableSampleRates[AvailableSampleRateCount++] = sampleRate;
        return true;
    }
}
