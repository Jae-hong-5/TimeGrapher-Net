using TimeGrapher.App.Services;
using TimeGrapher.Core.AudioIo;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class PlaybackFileServiceTests
{
    [Fact]
    public async Task CancelledPickerReturnsNotSelected()
    {
        var dialogs = new FakeDialogs((string?)null);
        var service = new PlaybackFileService(dialogs);

        PlaybackFileSelectionResult result = await service.SelectPlaybackFileAsync("C:\\start");

        Assert.False(result.Selected);
        Assert.Null(result.FilePath);
        Assert.Equal("C:\\start", result.CurrentDirectory);
        Assert.Equal("", result.StatusMessage);
    }

    [Fact]
    public async Task MissingFileThenCancelReturnsLastStatusMessage()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".wav");
        var dialogs = new FakeDialogs(missing, null);
        var errorLog = new FakeUserErrorLog();
        var service = new PlaybackFileService(dialogs, errorLog);

        PlaybackFileSelectionResult result = await service.SelectPlaybackFileAsync("C:\\start");

        Assert.False(result.Selected);
        Assert.Equal(UserErrorMessages.PlaybackFileOpenFailed, result.StatusMessage);
        Assert.Equal(2, dialogs.OpenPickerCalls);
        var entry = Assert.Single(errorLog.Entries);
        Assert.Equal(UserErrorMessages.PlaybackFileOpenFailed, entry.UserMessage);
        Assert.Contains(missing, entry.Detail);
    }

    [Fact]
    public async Task NonStandardWavShowsErrorAndKeepsPrompting()
    {
        string path = CreateFloatMonoWav(sampleRate: 44100);
        try
        {
            var dialogs = new FakeDialogs(path, null);
            var errorLog = new FakeUserErrorLog();
            var service = new PlaybackFileService(dialogs, errorLog);

            PlaybackFileSelectionResult result = await service.SelectPlaybackFileAsync(Path.GetTempPath());

            Assert.False(result.Selected);
            Assert.Equal(UserErrorMessages.PlaybackFileUnsupported, result.StatusMessage);
            Assert.Equal(new[] { (UserErrorMessages.DialogTitle, UserErrorMessages.PlaybackFileUnsupported) }, dialogs.Errors);
            Assert.Equal(2, dialogs.OpenPickerCalls);
            var entry = Assert.Single(errorLog.Entries);
            Assert.Equal(UserErrorMessages.PlaybackFileUnsupported, entry.UserMessage);
            Assert.Contains("sample_rate=44100", entry.Detail);
            Assert.Contains("channels=1", entry.Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidThenValidFileReturnsValidSelection()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".wav");
        string valid = CreateFloatMonoWav(sampleRate: 96000);
        try
        {
            var dialogs = new FakeDialogs(missing, valid);
            var errorLog = new FakeUserErrorLog();
            var service = new PlaybackFileService(dialogs, errorLog);

            PlaybackFileSelectionResult result = await service.SelectPlaybackFileAsync("C:\\start");

            Assert.True(result.Selected);
            Assert.Equal(valid, result.FilePath);
            Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(valid)), result.CurrentDirectory);
            Assert.Equal(96000, result.SampleRate);
            Assert.Null(result.StatusMessage);
            Assert.Equal(2, dialogs.OpenPickerCalls);
            Assert.Single(errorLog.Entries);
        }
        finally
        {
            File.Delete(valid);
        }
    }

    private static string CreateFloatMonoWav(int sampleRate)
    {
        string path = Path.Combine(Path.GetTempPath(), "timegrapher-playback-file-" + Guid.NewGuid().ToString("N") + ".wav");
        using var writer = new WavStreamWriter();
        Assert.True(writer.Open(path, sampleRate, channels: 1));
        Assert.True(writer.Write(new[] { 0.1f, -0.1f, 0.2f, -0.2f }));
        Assert.True(writer.Close());
        return path;
    }

    private sealed class FakeDialogs : ITimeGrapherDialogService
    {
        private readonly Queue<string?> _openResults;

        public FakeDialogs(params string?[] openResults)
        {
            _openResults = new Queue<string?>(openResults);
        }

        public int OpenPickerCalls { get; private set; }
        public List<(string Title, string Message)> Errors { get; } = new();

        public Task<RecordSessionChoice> AskRecordSessionAsync() => Task.FromResult(RecordSessionChoice.No);

        public Task<string?> PickOpenWavAsync(string currentDirectory)
        {
            _ = currentDirectory;
            OpenPickerCalls++;
            return Task.FromResult(_openResults.Count == 0 ? null : _openResults.Dequeue());
        }

        public Task<string?> PickOpenMeasurementLogAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickSaveWavAsync() => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<AiExplanationDialogResult?> AskAiExplanationAsync(AiExplanationDialogRequest request) => Task.FromResult<AiExplanationDialogResult?>(null);

        public Task<IAiExplanationDisplaySession> ShowAiExplanationProgressAsync(AiExplanationProgressDisplay display) =>
            Task.FromResult<IAiExplanationDisplaySession>(new FakeAiExplanationDisplaySession());
    }

    private sealed class FakeAiExplanationDisplaySession : IAiExplanationDisplaySession
    {
        public Task ShowStatusAsync(string statusText) => Task.CompletedTask;
        public Task ShowResultAsync(AiExplanationDisplay display) => Task.CompletedTask;
        public Task ShowFailureAsync(AiExplanationFailureDisplay failure) => Task.CompletedTask;
    }
}
