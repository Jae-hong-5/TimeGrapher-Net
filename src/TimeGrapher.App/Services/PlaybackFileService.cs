using TimeGrapher.Core.AudioIo;

namespace TimeGrapher.App.Services;

internal sealed class PlaybackFileService
{
    private readonly ITimeGrapherDialogService _dialogs;
    private readonly IUserErrorLog _errorLog;

    public PlaybackFileService(ITimeGrapherDialogService dialogs, IUserErrorLog? errorLog = null)
    {
        _dialogs = dialogs;
        _errorLog = errorLog ?? NullUserErrorLog.Instance;
    }

    public async Task<PlaybackFileSelectionResult> SelectPlaybackFileAsync(string currentDirectory)
    {
        string statusMessage = "";
        while (true)
        {
            string? picked = await _dialogs.PickOpenWavAsync(currentDirectory);
            if (picked == null)
            {
                return new PlaybackFileSelectionResult(false, null, currentDirectory, 0, statusMessage);
            }

            PlaybackFileValidationResult validation = await ValidateAsync(picked);
            if (!validation.IsValid)
            {
                statusMessage = validation.StatusMessage;
                continue;
            }

            string nextDirectory = currentDirectory;
            try
            {
                nextDirectory = Path.GetDirectoryName(Path.GetFullPath(picked)) ?? currentDirectory;
            }
            catch
            {
            }

            return new PlaybackFileSelectionResult(true, picked, nextDirectory, validation.SampleRate, null);
        }
    }

    private async Task<PlaybackFileValidationResult> ValidateAsync(string fileName)
    {
        if (!File.Exists(fileName))
        {
            _errorLog.Write(
                UserErrorMessages.PlaybackFileOpenFailed,
                "Playback file does not exist: " + ToNativeSeparators(fileName));
            return PlaybackFileValidationResult.Invalid(UserErrorMessages.PlaybackFileOpenFailed);
        }

        if (!WavProbe.TryReadFormat(fileName, out WavFormatInfo format, out string probeError))
        {
            _errorLog.Write(
                UserErrorMessages.PlaybackFileOpenFailed,
                "WAV probe failed for " + ToNativeSeparators(fileName) + ": " + probeError);
            return PlaybackFileValidationResult.Invalid(UserErrorMessages.PlaybackFileOpenFailed);
        }

        if (!WavProbe.IsAccepted(format, WavAcceptanceProfile.PlaybackFloatMonoStandardRates))
        {
            _errorLog.Write(
                UserErrorMessages.PlaybackFileUnsupported,
                "Unsupported playback WAV: path=" + ToNativeSeparators(fileName) +
                ", audio_format=" + format.AudioFormat +
                ", channels=" + format.NumChannels +
                ", sample_rate=" + format.SampleRate +
                ", bits_per_sample=" + format.BitsPerSample +
                ", block_align=" + format.BlockAlign +
                ", data_size=" + format.DataSize);
            await _dialogs.ShowErrorAsync(UserErrorMessages.DialogTitle, UserErrorMessages.PlaybackFileUnsupported);
            return PlaybackFileValidationResult.Invalid(UserErrorMessages.PlaybackFileUnsupported);
        }

        return PlaybackFileValidationResult.Valid(format.SampleRate);
    }

    private static string ToNativeSeparators(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private readonly record struct PlaybackFileValidationResult(bool IsValid, int SampleRate, string StatusMessage)
    {
        public static PlaybackFileValidationResult Valid(int sampleRate)
        {
            return new PlaybackFileValidationResult(true, sampleRate, "");
        }

        public static PlaybackFileValidationResult Invalid(string statusMessage)
        {
            return new PlaybackFileValidationResult(false, 0, statusMessage);
        }
    }
}
