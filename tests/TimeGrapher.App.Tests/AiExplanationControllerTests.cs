using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AiExplanationControllerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string file in _tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task ExplainAsync_ProbeFailureDisablesPersistenceButStillSendsRequest()
    {
        string logPath = WriteTempLog("rate_valid,rate_s_per_day\ntrue,3.2");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiExplanationDialogResult(
                AiExplanationService.PrimaryBackendBaseUrl,
                "grader",
                "secret",
                RememberCredentials: false,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore { ProbeResult = false };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(dialogs, ai, store);

        await controller.ExplainAsync();

        Assert.False(dialogs.LastDialogRequest!.CredentialPersistenceAvailable);
        Assert.Equal("rate_valid,rate_s_per_day\ntrue,3.2", ai.LastLogText);
        Assert.Equal("grader", ai.LastCredentials!.Username);
        Assert.NotNull(dialogs.LastDisplay);
        Assert.Null(store.SavedCredentials);
    }

    [Fact]
    public async Task ExplainAsync_RememberCredentialsSavesToCredentialStore()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiExplanationDialogResult(
                AiExplanationService.AwsBackendBaseUrl,
                "grader",
                "secret",
                RememberCredentials: true,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore
        {
            ProbeResult = true,
            ReadResult = new AiBackendCredentials("saved", "saved-secret")
        };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(dialogs, ai, store);

        await controller.ExplainAsync();

        Assert.True(dialogs.LastDialogRequest!.CredentialPersistenceAvailable);
        Assert.Equal("saved", dialogs.LastDialogRequest.SavedCredentials!.Username);
        Assert.Equal("grader", store.SavedCredentials!.Username);
        Assert.Equal(AiExplanationService.AwsBackendBaseUrl, ai.LastBackendBaseUrl);
    }

    [Fact]
    public async Task ExplainAsync_DeclinedConsentDoesNotCallBackend()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = null
        };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.ExplainAsync();

        Assert.Null(dialogs.LastDisplay);
        Assert.Null(ai.LastLogText);
    }

    private string WriteTempLog(string text)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, text);
        _tempFiles.Add(path);
        return path;
    }

    private sealed class FakeDialogs : ITimeGrapherDialogService
    {
        public string? MeasurementLogPath { get; init; }
        public AiExplanationDialogResult? DialogResult { get; init; }
        public AiExplanationDialogRequest? LastDialogRequest { get; private set; }
        public AiExplanationDisplay? LastDisplay { get; private set; }
        public List<(string Title, string Message)> Errors { get; } = new();

        public Task<RecordSessionChoice> AskRecordSessionAsync() => Task.FromResult(RecordSessionChoice.Cancel);

        public Task<string?> PickOpenWavAsync(string currentDirectory) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenMeasurementLogAsync() => Task.FromResult(MeasurementLogPath);

        public Task<string?> PickSaveWavAsync() => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<AiExplanationDialogResult?> AskAiExplanationAsync(AiExplanationDialogRequest request)
        {
            LastDialogRequest = request;
            return Task.FromResult(DialogResult);
        }

        public Task ShowAiExplanationAsync(AiExplanationDisplay display)
        {
            LastDisplay = display;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCredentialStore : IAiCredentialStore
    {
        public bool ProbeResult { get; init; }
        public AiBackendCredentials? ReadResult { get; init; }
        public AiBackendCredentials? SavedCredentials { get; private set; }
        public bool DeleteCalled { get; private set; }

        public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(ProbeResult);

        public Task<AiBackendCredentials?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(ReadResult);

        public Task<bool> SaveAsync(AiBackendCredentials credentials, CancellationToken cancellationToken)
        {
            SavedCredentials = credentials;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAiExplanationService : IAiExplanationService
    {
        public string? LastBackendBaseUrl { get; private set; }
        public string? LastLogText { get; private set; }
        public AiBackendCredentials? LastCredentials { get; private set; }

        public Task<AiExplanationResult> ExplainMeasurementLogAsync(
            string backendBaseUrl,
            string logText,
            AiBackendCredentials credentials,
            CancellationToken cancellationToken)
        {
            LastBackendBaseUrl = backendBaseUrl;
            LastLogText = logText;
            LastCredentials = credentials;
            return Task.FromResult(new AiExplanationResult("rid", "설명", "gemini-test"));
        }
    }
}
