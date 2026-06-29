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
            status => status.Contains("Waiting for AI response", StringComparison.Ordinal));
        Assert.Null(dialogs.LastDisplay);

        response.SetResult(new AiAnalysisResult("rid-progress", "응답", "gemini-test"));
        await run;

        Assert.Equal("응답", dialogs.LastDisplay!.Explanation);
    }

    [Fact]
    public async Task AnalyzeAsync_ClosingProgressWindowCancelsInFlightRequest()
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
        var ai = new FakeAiAnalysisService
        {
            RequestStarted = requestStarted,
            AwaitCancellation = true
        };
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        Task run = controller.AnalyzeAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // The request is in flight; closing the progress window must cancel its token.
        dialogs.LastDisplaySession!.RaiseClosed();
        await run.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(ai.LastCancellationToken.IsCancellationRequested);
        Assert.Null(dialogs.LastDisplay);
        Assert.NotNull(dialogs.LastFailure);
    }

    [Fact]
    public async Task AnalyzeAsync_ClosingResultWindowAfterSuccessDoesNotCancelCompletedRequest()
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
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(
            dialogs,
            ai,
            new FakeCredentialStore { ProbeResult = true });

        await controller.AnalyzeAsync();

        Assert.NotNull(dialogs.LastDisplay);
        Exception? ex = Record.Exception(() => dialogs.LastDisplaySession!.RaiseClosed());
        Assert.Null(ex);
        Assert.False(ai.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AnalyzeAsync_ClosingResultWindowDuringCredentialSaveDoesNotFault()
    {
        string logPath = WriteTempLog("log");
        var dialogs = new FakeDialogs
        {
            MeasurementLogPath = logPath,
            DialogResult = new AiAnalysisDialogResult(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "grader",
                "secret",
                RememberCredentials: true,
                ConsentGranted: true)
        };
        var store = new FakeCredentialStore
        {
            ProbeResult = true,
            AwaitSaveCancellation = true
        };
        var controller = new AiAnalysisController(
            dialogs,
            new FakeAiAnalysisService(),
            store);

        Task run = controller.AnalyzeAsync();
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(dialogs.LastDisplay);
        dialogs.LastDisplaySession!.RaiseClosed();
        await run.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(store.SaveCancellationObserved);
        Assert.Empty(dialogs.Errors);
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
                "User ID or User PW is incorrect.")
        };
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.Null(store.SavedCredentials);
        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("User ID or User PW is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
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
                "User ID or User PW is incorrect.")
        };
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        Assert.False(store.DeleteCalled);
        Assert.Null(dialogs.LastDisplay);
        Assert.Contains("User ID or User PW is incorrect.", dialogs.LastFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_UncheckedRememberSurfacesErrorWhenDeleteFails()
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
            ReadResult = new AiBackendCredentials("saved", "saved-secret"),
            DeleteResult = false
        };
        var ai = new FakeAiAnalysisService();
        var controller = new AiAnalysisController(dialogs, ai, store);

        await controller.AnalyzeAsync();

        // The backend call still succeeded (the result is shown), but the credential
        // removal failed, so the controller must surface the credential-update error
        // path instead of silently reporting success.
        Assert.True(store.DeleteCalled);
        Assert.NotNull(dialogs.LastDisplay);
        Assert.Contains(
            dialogs.Errors,
            error => error.Message.Contains("could not be removed", StringComparison.Ordinal));
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
        private Action? _onClosed;

        public List<string> StatusTexts { get; } = new();
        public AiAnalysisDisplay? LastDisplay { get; private set; }
        public AiAnalysisFailureDisplay? LastFailure { get; private set; }

        public void OnClosed(Action callback) => _onClosed += callback;

        public void RaiseClosed() => _onClosed?.Invoke();

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
        public bool DeleteResult { get; init; } = true;
        public AiBackendCredentials? SavedCredentials { get; private set; }
        public bool DeleteCalled { get; private set; }
        public bool AwaitSaveCancellation { get; init; }
        public bool SaveCancellationObserved { get; private set; }
        public TaskCompletionSource<bool> SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(ProbeResult);

        public Task<AiBackendCredentials?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(ReadResult);

        public async Task<bool> SaveAsync(AiBackendCredentials credentials, CancellationToken cancellationToken)
        {
            SavedCredentials = credentials;
            if (AwaitSaveCancellation)
            {
                SaveStarted.TrySetResult(true);
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    await cancelled.Task;
                }

                SaveCancellationObserved = true;
                cancellationToken.ThrowIfCancellationRequested();
            }

            return true;
        }

        public Task<bool> DeleteAsync(CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class FakeAiAnalysisService : IAiAnalysisService
    {
        public string? LastBackendBaseUrl { get; private set; }
        public string? LastLogText { get; private set; }
        public AiBackendCredentials? LastCredentials { get; private set; }
        public bool? LastConsentGranted { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public AiAnalysisServiceException? Exception { get; init; }
        public TaskCompletionSource<bool>? RequestStarted { get; init; }
        public Task<AiAnalysisResult>? ResultTask { get; init; }
        public bool AwaitCancellation { get; init; }

        public async Task<AiAnalysisResult> AnalyzeMeasurementLogAsync(
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
            LastCancellationToken = cancellationToken;
            RequestStarted?.TrySetResult(true);

            if (Exception != null)
            {
                throw Exception;
            }

            if (AwaitCancellation)
            {
                // Block until the request token is cancelled (the progress window
                // closing), then surface cancellation like the real HTTP client would.
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => tcs.TrySetResult(true)))
                {
                    await tcs.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return ResultTask != null
                ? await ResultTask
                : new AiAnalysisResult("rid", "설명", "gemini-test");
        }
    }
}
