using System.Collections.Generic;

using TimeGrapher.App.Audio;
using TimeGrapher.App.Services;
using TimeGrapher.Core.Shared;

namespace TimeGrapher.App.Views;

/// <summary>The view-side <see cref="IAudioDeviceBackend"/> wrapping the platform LiveAudioBackend,
/// so the AudioDeviceController stays platform-agnostic.</summary>
internal sealed class LiveAudioDeviceBackend : IAudioDeviceBackend
{
    public bool CanCapture => LiveAudioBackend.CanCapture;

    public IReadOnlyList<LiveAudioDevice> EnumerateInputDevices() => LiveAudioBackend.EnumerateInputDevices();

    public IReadOnlyList<int> GetCandidateSampleRates(int deviceNumber) => LiveAudioBackend.GetCandidateSampleRates(deviceNumber);
}
