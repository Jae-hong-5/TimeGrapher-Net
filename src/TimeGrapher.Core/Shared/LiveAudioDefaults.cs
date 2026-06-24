namespace TimeGrapher.Core.Shared;

/// <summary>
/// Defaults for the live-capture path shared across the platform workers and the
/// settings that drive them. The capture buffer length is the one platform-specific
/// knob: on Windows it maps directly to NAudio's <c>BufferMilliseconds</c>; on Linux
/// it maps to the capture process's latency flag, but only when the user changes it
/// from this default, so the default reproduces each backend's native buffering.
/// </summary>
public static class LiveAudioDefaults
{
    /// <summary>Default capture buffer length in milliseconds (the historical Windows <c>BufferMilliseconds</c>).</summary>
    public const int BufferMilliseconds = 20;
}
