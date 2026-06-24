namespace TimeGrapher.App.Services;

internal static class UserErrorMessages
{
    public const string DialogTitle = "Please check";
    public const string SelectLiveAudioDevice = "Please select a live audio device before starting.";
    public const string SelectSampleRate = "Please select a sample rate before starting.";
    public const string CouldNotStartLiveAudio = "We couldn't start live audio. Please check your device and try again.";
    public const string CouldNotStartRun = "We couldn't start the run. Please try again.";
    public const string LiveAudioStopped = "Live audio stopped unexpectedly. Please check your device connection.";
    public const string PlaybackStoppedWithError = "Playback stopped unexpectedly. Please try again.";
    public const string SimulationStoppedWithError = "Simulation stopped unexpectedly. Please try again.";
    public const string StopDidNotFinish = "Stop could not finish. Please press Stop and try again.";
    public const string RecordingCloseFailed = "We couldn't finish saving the WAV recording. Please try again.";
    public const string RecordingMayBeIncomplete = "The WAV recording may be incomplete. Please check the saved file.";
    public const string RecordingOpenFailed = "We couldn't start WAV recording. Please choose a different save location.";
    public const string PlaybackFileOpenFailed = "We couldn't open the selected WAV file. Please choose another file.";
    public const string PlaybackFileUnsupported = "This WAV file isn't supported. Please choose a supported WAV file.";
    public const string ManualOpenFailed = "We couldn't open the manual in a browser. Please try again.";
    public const string AudioInputInterrupted = "Audio input was interrupted. Please check your device connection.";
    public const string AnalysisRunningBehind = "Analysis is running behind. Measurement may update more slowly.";
    public const string DisplayQualityReduced = "Display quality was reduced to keep measurements responsive.";
}
