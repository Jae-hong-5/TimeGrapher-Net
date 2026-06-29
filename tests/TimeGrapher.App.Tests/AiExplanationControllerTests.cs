using System.Net;
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
        Assert.True(ai.LastConsentGranted);
        Assert.NotNull(dialogs.LastDisplay);
        Assert.Null(store.SavedCredentials);
    }

    [Fact]
    public async Task ExplainAsync_ShowsProgressWindowBeforeBackendCompletes()
    {
        string logPath = WriteTempLog("log");
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
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<AiExplanationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ai = new FakeAiExplanationService
        {
            RequestStarted = requestStarted,
            ResultTask = response.Task
        };
        var controller = new AiExplanationController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        Task run = controller.ExplainAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(dialogs.LastProgressDisplay);
        Assert.Equal(AiExplanationService.PrimaryBackendBaseUrl, dialogs.LastProgressDisplay.BackendBaseUrl);
        Assert.NotNull(dialogs.LastDisplaySession);
        Assert.Contains(
            dialogs.LastDisplaySession.StatusTexts,
            status => status.Contains("Waiting for response", StringComparison.Ordinal));
        Assert.Null(dialogs.LastDisplay);

        response.SetResult(new AiExplanationResult("rid-progress", "응답", "gemini-test"));
        await run;

        Assert.Equal("응답", dialogs.LastDisplay!.Explanation);
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
    public async Task ExplainAsync_RememberCredentialsDoesNotSaveWhenBackendRejectsLogin()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiExplanationDialogResult(
                AiExplanationService.AwsBackendBaseUrl,
                "grader",
                "wrong",
                RememberCredentials: true,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore
        {
            ProbeResult = true,
            ReadResult = new AiBackendCredentials("saved", "saved-secret")
        };
        var ai = new FakeAiExplanationService
        {
            Exception = new AiExplanationServiceException(
                HttpStatusCode.Unauthorized,
                "rid",
                "unauthorized",
                "Demo username or password is incorrect.")
        };
        var controller = new AiExplanationController(dialogs, ai, store);

        await controller.ExplainAsync();

        Assert.Null(store.SavedCredentials);
        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("Demo username or password is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_UncheckedRememberDeletesSavedCredentialsAfterSuccessfulBackendCall()
    {
        string logPath = WriteTempLog("log");
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
        var store = new FakeCredentialStore
        {
            ProbeResult = true,
            ReadResult = new AiBackendCredentials("saved", "saved-secret")
        };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(dialogs, ai, store);

        await controller.ExplainAsync();

        Assert.Equal("log", ai.LastLogText);
        Assert.True(store.DeleteCalled);
    }

    [Fact]
    public async Task ExplainAsync_UncheckedRememberDoesNotDeleteSavedCredentialsWhenBackendFails()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiExplanationDialogResult(
                AiExplanationService.PrimaryBackendBaseUrl,
                "grader",
                "wrong",
                RememberCredentials: false,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore
        {
            ProbeResult = true,
            ReadResult = new AiBackendCredentials("saved", "saved-secret")
        };
        var ai = new FakeAiExplanationService
        {
            Exception = new AiExplanationServiceException(
                HttpStatusCode.Unauthorized,
                "rid",
                "unauthorized",
                "Demo username or password is incorrect.")
        };
        var controller = new AiExplanationController(dialogs, ai, store);

        await controller.ExplainAsync();

        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("Demo username or password is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_OversizedLogDoesNotCallBackend()
    {
        string logPath = WriteTempLog(new string('x', AiExplanationService.MaxLogChars + 1));
        var dialogs = new FakeDialogs { MeasurementLogPath = logPath };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.ExplainAsync();

        Assert.Null(ai.LastLogText);
        Assert.Contains(dialogs.Errors, error => error.Message.Contains("too large", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplainAsync_UnsupportedExtensionDoesNotCallBackend()
    {
        string logPath = WriteTempLog("log", ".bin");
        var dialogs = new FakeDialogs { MeasurementLogPath = logPath };
        var ai = new FakeAiExplanationService();
        var controller = new AiExplanationController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.ExplainAsync();

        Assert.Null(ai.LastLogText);
        Assert.Contains(dialogs.Errors, error => error.Message.Contains(".csv", StringComparison.Ordinal));
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

    private string WriteTempLog(string text, string extension = ".csv")
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, text);
        _tempFiles.Add(path);
        return path;
    }

    private sealed class FakeDialogs : ITimeGrapherDialogService
    {
        public string? MeasurementLogPath { get; init; }
        public AiExplanationDialogResult? DialogResult { get; init; }
        public AiExplanationDialogRequest? LastDialogRequest { get; private set; }
        public AiExplanationProgressDisplay? LastProgressDisplay { get; private set; }
        public FakeAiExplanationDisplaySession? LastDisplaySession { get; private set; }
        public AiExplanationDisplay? LastDisplay => LastDisplaySession?.LastDisplay;
        public AiExplanationFailureDisplay? LastFailure => LastDisplaySession?.LastFailure;
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

        public Task<IAiExplanationDisplaySession> ShowAiExplanationProgressAsync(AiExplanationProgressDisplay display)
        {
            LastProgressDisplay = display;
            LastDisplaySession = new FakeAiExplanationDisplaySession();
            return Task.FromResult<IAiExplanationDisplaySession>(LastDisplaySession);
        }
    }

    private sealed class FakeAiExplanationDisplaySession : IAiExplanationDisplaySession
    {
        public List<string> StatusTexts { get; } = new();
        public AiExplanationDisplay? LastDisplay { get; private set; }
        public AiExplanationFailureDisplay? LastFailure { get; private set; }

        public Task ShowStatusAsync(string statusText)
        {
            StatusTexts.Add(statusText);
            return Task.CompletedTask;
        }

        public Task ShowResultAsync(AiExplanationDisplay display)
        {
            LastDisplay = display;
            return Task.CompletedTask;
        }

        public Task ShowFailureAsync(AiExplanationFailureDisplay failure)
        {
            LastFailure = failure;
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
        public bool? LastConsentGranted { get; private set; }
        public AiExplanationServiceException? Exception { get; init; }
        public TaskCompletionSource<bool>? RequestStarted { get; init; }
        public Task<AiExplanationResult>? ResultTask { get; init; }

        public Task<AiExplanationResult> ExplainMeasurementLogAsync(
            string backendBaseUrl,
            string logText,
            AiBackendCredentials credentials,
            bool consentGranted,
            CancellationToken cancellationToken)
        {
            LastBackendBaseUrl = backendBaseUrl;
            LastLogText = logText;
            LastCredentials = credentials;
            LastConsentGranted = consentGranted;
            RequestStarted?.TrySetResult(true);

            if (Exception != null)
            {
                throw Exception;
            }

            return ResultTask ?? Task.FromResult(new AiExplanationResult("rid", "설명", "gemini-test"));
        }
    }
}
