namespace TimeGrapher.App.Audio;

internal static class SimulationAudioDefaults
{
    // The realistic packet's narrow C-anchor reaches several adjacent samples at
    // 192 kHz. Keep the built-in simulation below the raw full-scale plateau
    // threshold so the clipping warning is reserved for deliberately overdriven
    // signals, not the default demo/practice source.
    public const double PcmPeakSignalLevel = 0.30;
}
