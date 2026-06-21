using System.Collections.Generic;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Services;

/// <summary>
/// The live-audio device queries the <see cref="AudioDeviceController"/> needs: capture
/// availability, input-device enumeration, and a device's candidate sample rates. A narrow seam
/// over the platform LiveAudioBackend so the controller stays platform-agnostic and testable.
/// </summary>
internal interface IAudioDeviceBackend
{
    bool CanCapture { get; }

    IReadOnlyList<LiveAudioDevice> EnumerateInputDevices();

    IReadOnlyList<int> GetCandidateSampleRates(int deviceNumber);
}
