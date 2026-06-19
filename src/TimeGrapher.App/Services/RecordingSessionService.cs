namespace TimeGrapher.App.Services;

internal readonly record struct RecordingSessionStartResult(bool ShouldContinue, IRecordingWriter? Writer);

internal sealed class RecordingSessionService
{
    private readonly ITimeGrapherDialogService _dialogs;
    private readonly IRecordingWriterFactory _writerFactory;
    private readonly IUserErrorLog _errorLog;

    public RecordingSessionService(
        ITimeGrapherDialogService dialogs,
        IRecordingWriterFactory writerFactory,
        IUserErrorLog? errorLog = null)
    {
        _dialogs = dialogs;
        _writerFactory = writerFactory;
        _errorLog = errorLog ?? NullUserErrorLog.Instance;
    }

    public async Task<RecordingSessionStartResult> TryStartAsync(int sampleRate)
    {
        RecordSessionChoice choice = await _dialogs.AskRecordSessionAsync();
        if (choice == RecordSessionChoice.No)
        {
            return new RecordingSessionStartResult(true, null);
        }

        if (choice == RecordSessionChoice.Cancel)
        {
            return new RecordingSessionStartResult(false, null);
        }

        string? fileName = await _dialogs.PickSaveWavAsync();
        if (string.IsNullOrEmpty(fileName))
        {
            return new RecordingSessionStartResult(false, null);
        }

        IRecordingWriter writer = _writerFactory.Create();
        if (!writer.Open(fileName, sampleRate, channels: 1))
        {
            _errorLog.Write(
                UserErrorMessages.RecordingOpenFailed,
                "Recording writer failed to open: path=" + fileName +
                ", sample_rate=" + sampleRate +
                ", channels=1");
            await _dialogs.ShowErrorAsync(UserErrorMessages.DialogTitle, UserErrorMessages.RecordingOpenFailed);
            writer.Dispose();
            return new RecordingSessionStartResult(false, null);
        }

        return new RecordingSessionStartResult(true, writer);
    }
}
