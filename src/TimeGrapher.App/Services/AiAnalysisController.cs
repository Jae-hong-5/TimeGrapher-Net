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

        // Tie the BACKEND request to the (non-modal) progress window: closing it cancels
        // the in-flight call. Credential reconciliation below runs afterward regardless
        // of the outcome and is intentionally NOT tied to this token (it uses
        // CancellationToken.None) so the user's "Remember" choice is always honored.
        using var requestLifetime = new AiAnalysisRequestLifetime();
        displaySession.OnClosed(requestLifetime.CancelIfActive);
        CancellationToken requestToken = requestLifetime.Token;

        await displaySession.ShowStatusAsync("Sending the selected measurement log to the selected server. Waiting for AI response.");

        AiAnalysisResult? result = null;
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
        }
        catch (TaskCanceledException)
        {
            await displaySession.ShowFailureAsync(new AiAnalysisFailureDisplay(
                "AI analysis request was canceled or timed out.",
                RequestId: null));
        }
        catch (OperationCanceledException)
        {
            await displaySession.ShowFailureAsync(new AiAnalysisFailureDisplay(
                "AI analysis request was canceled or timed out.",
                RequestId: null));
        }

        if (result != null)
        {
            await displaySession.ShowResultAsync(new AiAnalysisDisplay(
                result.RequestId,
                result.Explanation,
                result.Model,
                normalizedBackendBaseUrl));
        }

        // Reconcile saved credentials with the user's "Remember" choice REGARDLESS of
        // whether the analysis succeeded, so unchecking Remember reliably removes a
        // previously-saved login even when the request failed or the window was closed.
        // A new login is only persisted when the backend actually accepted it.
        string? credentialUpdateError = await ReconcileCredentialStoreAsync(
            dialogResult,
            credentials,
            credentialStoreAvailable,
            hadSavedCredentials: savedCredentials != null,
            persistOnSuccess: result != null);
        if (credentialUpdateError != null)
        {
            await _dialogs.ShowErrorAsync("AI Analysis", credentialUpdateError);
        }
    }

    private async Task<string?> ReconcileCredentialStoreAsync(
        AiAnalysisDialogResult dialogResult,
        AiBackendCredentials credentials,
        bool credentialStoreAvailable,
        bool hadSavedCredentials,
        bool persistOnSuccess)
    {
        if (!credentialStoreAvailable)
        {
            return null;
        }

        // Use CancellationToken.None (not the window-close token): the user's persistence
        // choice must be applied even if they closed the progress/result window.
        try
        {
            if (dialogResult.RememberCredentials)
            {
                // Only persist a login the backend actually accepted.
                if (!persistOnSuccess)
                {
                    return null;
                }

                bool saved = await _credentialStore.SaveAsync(credentials, CancellationToken.None);
                return saved
                    ? null
                    : "User ID / User PW could not be saved by this device. This request completed without changing saved login state.";
            }

            // "Remember" is unchecked: remove any previously-saved login. If nothing was
            // saved there is nothing to remove and no failure to report - this also
            // avoids a spurious "could not be removed" error on a store that returns
            // false for a missing entry (e.g. first-time use on Windows).
            if (!hadSavedCredentials)
            {
                return null;
            }

            bool deleted = await _credentialStore.DeleteAsync(CancellationToken.None);
            return deleted
                ? null
                : "Saved login could not be removed by the operating system credential store. This request completed without changing saved login state.";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return "Saved login state could not be updated on this device.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Saved login state could not be updated on this device.";
        }
    }

    private sealed class AiAnalysisRequestLifetime : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _source = new();
        private bool _active = true;

        public CancellationToken Token => _source.Token;

        public void CancelIfActive()
        {
            lock (_gate)
            {
                if (_active)
                {
                    _source.Cancel();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _active = false;
                _source.Dispose();
            }
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
