using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed class AiAnalysisController : IAiAnalysisRunner
{
    private static readonly HashSet<string> MeasurementLogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv",
        ".log",
        ".txt"
    };

    private readonly ITimeGrapherDialogService _dialogs;
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly IAiCredentialStore _credentialStore;

    public AiAnalysisController(
        ITimeGrapherDialogService dialogs,
        IAiAnalysisService aiAnalysisService,
        IAiCredentialStore credentialStore)
    {
        _dialogs = dialogs;
        _aiAnalysisService = aiAnalysisService;
        _credentialStore = credentialStore;
    }

    public async Task AnalyzeAsync()
    {
        try
        {
            await AnalyzeCoreAsync();
        }
        catch (AiAnalysisServiceException ex)
        {
            await ShowServiceErrorAsync(ex);
        }
        catch (UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "Selected measurement log could not be read.");
        }
        catch (IOException)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "Selected measurement log could not be read.");
        }
        catch (TaskCanceledException)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "AI analysis request was canceled or timed out.");
        }
        catch (ArgumentException)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "AI analysis settings were invalid.");
        }
    }

    private async Task AnalyzeCoreAsync()
    {
        string? logPath = await _dialogs.PickOpenMeasurementLogAsync();
        if (logPath == null)
        {
            return;
        }

        if (!File.Exists(logPath))
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "Selected measurement log does not exist.");
            return;
        }

        if (!MeasurementLogExtensions.Contains(Path.GetExtension(logPath)))
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "Select a measurement log file (.csv, .log, or .txt).");
            return;
        }

        var logFile = new FileInfo(logPath);
        if (logFile.Length > AiAnalysisService.MaxLogFileBytes)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", LogTooLargeMessage());
            return;
        }

        string logText = await File.ReadAllTextAsync(logPath);
        if (string.IsNullOrWhiteSpace(logText))
        {
            await _dialogs.ShowErrorAsync("AI Analysis", "Selected measurement log is empty.");
            return;
        }

        if (logText.Length > AiAnalysisService.MaxLogChars)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", LogTooLargeMessage());
            return;
        }

        bool credentialStoreAvailable = await _credentialStore.ProbeAsync(CancellationToken.None);
        AiBackendCredentials? savedCredentials = credentialStoreAvailable
            ? await _credentialStore.ReadAsync(CancellationToken.None)
            : null;

        var request = new AiAnalysisDialogRequest(
            AiAnalysisService.BackendOptions,
            AiAnalysisService.PrimaryBackendBaseUrl,
            savedCredentials,
            credentialStoreAvailable);
        AiAnalysisDialogResult? dialogResult = await _dialogs.AskAiAnalysisAsync(request);
        if (dialogResult == null || !dialogResult.ConsentGranted)
        {
            return;
        }

        string normalizedBackendBaseUrl = AiAnalysisService.NormalizeApprovedBackendBaseUrl(dialogResult.BackendBaseUrl);
        var credentials = new AiBackendCredentials(dialogResult.Username, dialogResult.Password);
        IAiAnalysisDisplaySession displaySession = await _dialogs.ShowAiAnalysisProgressAsync(
            new AiAnalysisProgressDisplay(
                normalizedBackendBaseUrl,
                "Preparing AI analysis request."));

        // Tie the request lifetime to the (non-modal) progress window: closing it
        // cancels the in-flight backend call and the credential-store update.
        using var requestCts = new CancellationTokenSource();
        displaySession.OnClosed(requestCts.Cancel);
        CancellationToken requestToken = requestCts.Token;

        await displaySession.ShowStatusAsync("Sending selected measurement log to the AI backend. Waiting for response.");

        AiAnalysisResult result;
        try
        {
            result = await _aiAnalysisService.AnalyzeMeasurementLogAsync(
                normalizedBackendBaseUrl,
                logText,
                credentials,
                dialogResult.ConsentGranted,
                requestToken);
        }
        catch (AiAnalysisServiceException ex)
        {
            await displaySession.ShowFailureAsync(ToFailureDisplay(ex));
            return;
        }
        catch (TaskCanceledException)
        {
            await displaySession.ShowFailureAsync(new AiAnalysisFailureDisplay(
                "AI analysis request was canceled or timed out.",
                RequestId: null));
            return;
        }
        catch (OperationCanceledException)
        {
            await displaySession.ShowFailureAsync(new AiAnalysisFailureDisplay(
                "AI analysis request was canceled or timed out.",
                RequestId: null));
            return;
        }

        await displaySession.ShowResultAsync(new AiAnalysisDisplay(
            result.RequestId,
            result.Explanation,
            result.Model,
            normalizedBackendBaseUrl));

        string? credentialUpdateError = await UpdateCredentialStoreAfterSuccessAsync(
            dialogResult,
            credentials,
            credentialStoreAvailable,
            requestToken);
        if (credentialUpdateError != null)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", credentialUpdateError);
        }
    }

    private async Task<string?> UpdateCredentialStoreAfterSuccessAsync(
        AiAnalysisDialogResult dialogResult,
        AiBackendCredentials credentials,
        bool credentialStoreAvailable,
        CancellationToken cancellationToken)
    {
        if (!credentialStoreAvailable)
        {
            return null;
        }

        try
        {
            if (dialogResult.RememberCredentials)
            {
                bool saved = await _credentialStore.SaveAsync(credentials, cancellationToken);
                return saved
                    ? null
                    : "Credentials could not be saved by the operating system credential store. This request completed without changing saved login state.";
            }

            bool deleted = await _credentialStore.DeleteAsync(cancellationToken);
            return deleted
                ? null
                : "Saved login could not be removed by the operating system credential store. This request completed without changing saved login state.";
        }
        catch (IOException)
        {
            return "Saved login state could not be updated by the operating system credential store.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Saved login state could not be updated by the operating system credential store.";
        }
    }

    private async Task ShowServiceErrorAsync(AiAnalysisServiceException ex)
    {
        AiAnalysisFailureDisplay failure = ToFailureDisplay(ex);
        string requestIdText = failure.RequestId == null
            ? string.Empty
            : $"\n\nRequest ID: {failure.RequestId}";
        await _dialogs.ShowErrorAsync("AI Analysis", failure.Message + requestIdText);
    }

    private static AiAnalysisFailureDisplay ToFailureDisplay(AiAnalysisServiceException ex) => new(
        ex.Message,
        string.IsNullOrWhiteSpace(ex.RequestId) ? null : ex.RequestId);

    private static string LogTooLargeMessage() =>
        "Measurement log is too large. Select a log under " +
        AiAnalysisService.MaxLogChars.ToString("N0") +
        " characters.";
}
