namespace TimeGrapher.Core.Shared;

public interface ILiveAudioWorker : IAudioInputWorker
{
    /// <summary>Raised on the capture thread when capture ends without a stop request
    /// (e.g. the device disappears or the capture process exits).</summary>
    event Action? CaptureEnded;

    void Start(int deviceNumber, int sampleRate, float volume, int bufferMilliseconds);

    void SetVolume(float volume);
}
