using System.Net;
using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AiAnalysisControllerTests : IDisposable
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
    public async Task AnalyzeAsync_ProbeFailureDisablesPersistenceButStillSendsRequest()
    {
        string logPath = WriteTempLog("rate_valid,rate_s_per_day\ntrue,3.2");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "grader",
                "secret",
                RememberCredentials: false,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore { ProbeResult = false };
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.False(dialogs.LastDialogRequest!.CredentialPersistenceAvailable);
        Assert.Equal("rate_valid,rate_s_per_day\ntrue,3.2", ai.LastLogText);
        Assert.Equal("grader", ai.LastCredentials!.Username);
        Assert.True(ai.LastConsentGranted);
        Assert.NotNull(dialogs.LastDisplay);
        Assert.Null(store.SavedCredentials);
    }

    [Fact]
    public async Task AnalyzeAsync_ShowsProgressWindowBeforeBackendCompletes()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "grader",
                "secret",
                RememberCredentials: false,
                ConsentGranted: true)
        };
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<AiAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ai = new FakeAiAnalysisService
        {
            RequestStarted = requestStarted,
            ResultTask = response.Task
        };
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        Task run = controller.AnalyzeAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(dialogs.LastProgressDisplay);
        Assert.Equal(AiAnalysisService.PrimaryBackendBaseUrl, dialogs.LastProgressDisplay.BackendBaseUrl);
        Assert.NotNull(dialogs.LastDisplaySession);
        Assert.Contains(
            dialogs.LastDisplaySession.StatusTexts,
            status => status.Contains("Waiting for response", StringComparison.Ordinal));
        Assert.Null(dialogs.LastDisplay);

        response.SetResult(new AiAnalysisResult("rid-progress", "응답", "gemini-test"));
        await run;

        Assert.Equal("응답", dialogs.LastDisplay!.Explanation);
    }

    [Fact]
    public async Task AnalyzeAsync_RememberCredentialsSavesToCredentialStore()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.AwsBackendBaseUrl,
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
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.True(dialogs.LastDialogRequest!.CredentialPersistenceAvailable);
        Assert.Equal("saved", dialogs.LastDialogRequest.SavedCredentials!.Username);
        Assert.Equal("grader", store.SavedCredentials!.Username);
        Assert.Equal(AiAnalysisService.AwsBackendBaseUrl, ai.LastBackendBaseUrl);
    }

    [Fact]
    public async Task AnalyzeAsync_RememberCredentialsDoesNotSaveWhenBackendRejectsLogin()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.AwsBackendBaseUrl,
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
        var ai = new FakeAiAnalysisService
        {
            Exception = new AiAnalysisServiceException(
                HttpStatusCode.Unauthorized,
                "rid",
                "unauthorized",
                "Demo username or password is incorrect.")
        };
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.Null(store.SavedCredentials);
        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("Demo username or password is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_UncheckedRememberDeletesSavedCredentialsAfterSuccessfulBackendCall()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.PrimaryBackendBaseUrl,
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
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.Equal("log", ai.LastLogText);
        Assert.True(store.DeleteCalled);
    }

    [Fact]
    public async Task AnalyzeAsync_UncheckedRememberDoesNotDeleteSavedCredentialsWhenBackendFails()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.PrimaryBackendBaseUrl,
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
        var ai = new FakeAiAnalysisService
        {
            Exception = new AiAnalysisServiceException(
                HttpStatusCode.Unauthorized,
                "rid",
                "unauthorized",
                "Demo username or password is incorrect.")
        };
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("Demo username or password is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_OversizedLogDoesNotCallBackend()
    {
        string logPath = WriteTempLog(new string('x', AiAnalysisService.MaxLogChars + 1));
        var dialogs = new FakeDialogs { MeasurementLogPath = logPath };
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.AnalyzeAsync();

        Assert.Null(ai.LastLogText);
        Assert.Contains(dialogs.Errors, error => error.Message.Contains("too large", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_UnsupportedExtensionDoesNotCallBackend()
    {
        string logPath = WriteTempLog("log", ".bin");
        var dialogs = new FakeDialogs { MeasurementLogPath = logPath };
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.AnalyzeAsync();

        Assert.Null(ai.LastLogText);
        Assert.Contains(dialogs.Errors, error => error.Message.Contains(".csv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_DeclinedConsentDoesNotCallBackend()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = null
        };
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.AnalyzeAsync();

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
        public AiAnalysisDialogResult? DialogResult { get; init; }
        public AiAnalysisDialogRequest? LastDialogRequest { get; private set; }
        public AiAnalysisProgressDisplay? LastProgressDisplay { get; private set; }
        public FakeAiAnalysisDisplaySession? LastDisplaySession { get; private set; }
        public AiAnalysisDisplay? LastDisplay => LastDisplaySession?.LastDisplay;
        public AiAnalysisFailureDisplay? LastFailure => LastDisplaySession?.LastFailure;
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

        public Task<AiAnalysisDialogResult?> AskAiAnalysisAsync(AiAnalysisDialogRequest request)
        {
            LastDialogRequest = request;
            return Task.FromResult(DialogResult);
        }

        public Task<IAiAnalysisDisplaySession> ShowAiAnalysisProgressAsync(AiAnalysisProgressDisplay display)
        {
            LastProgressDisplay = display;
            LastDisplaySession = new FakeAiAnalysisDisplaySession();
            return Task.FromResult<IAiAnalysisDisplaySession>(LastDisplaySession);
        }
    }

    private sealed class FakeAiAnalysisDisplaySession : IAiAnalysisDisplaySession
    {
        public List<string> StatusTexts { get; } = new();
        public AiAnalysisDisplay? LastDisplay { get; private set; }
        public AiAnalysisFailureDisplay? LastFailure { get; private set; }

        public Task ShowStatusAsync(string statusText)
        {
            StatusTexts.Add(statusText);
            return Task.CompletedTask;
        }

        public Task ShowResultAsync(AiAnalysisDisplay display)
        {
            LastDisplay = display;
            return Task.CompletedTask;
        }

        public Task ShowFailureAsync(AiAnalysisFailureDisplay failure)
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

    private sealed class FakeAiAnalysisService : IAiAnalysisService
    {
        public string? LastBackendBaseUrl { get; private set; }
        public string? LastLogText { get; private set; }
        public AiBackendCredentials? LastCredentials { get; private set; }
        public bool? LastConsentGranted { get; private set; }
        public AiAnalysisServiceException? Exception { get; init; }
        public TaskCompletionSource<bool>? RequestStarted { get; init; }
        public Task<AiAnalysisResult>? ResultTask { get; init; }

        public Task<AiAnalysisResult> AnalyzeMeasurementLogAsync(
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

            return ResultTask ?? Task.FromResult(new AiAnalysisResult("rid", "설명", "gemini-test"));
        }
    }
}
