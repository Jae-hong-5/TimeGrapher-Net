using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed class AiExplanationController : IAiExplanationRunner
{
    private static readonly HashSet<string> MeasurementLogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv",
        ".log",
        ".txt"
    };

    private readonly ITimeGrapherDialogService _dialogs;
    private readonly IAiExplanationService _aiExplanationService;
    private readonly IAiCredentialStore _credentialStore;

    public AiExplanationController(
        ITimeGrapherDialogService dialogs,
        IAiExplanationService aiExplanationService,
        IAiCredentialStore credentialStore)
    {
        _dialogs = dialogs;
        _aiExplanationService = aiExplanationService;
        _credentialStore = credentialStore;
    }

    public async Task ExplainAsync()
    {
        try
        {
            await ExplainCoreAsync();
        }
        catch (AiExplanationServiceException ex)
        {
            await ShowServiceErrorAsync(ex);
        }
        catch (UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Selected measurement log could not be read.");
        }
        catch (IOException)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Selected measurement log could not be read.");
        }
        catch (TaskCanceledException)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "AI explanation request was canceled or timed out.");
        }
        catch (ArgumentException)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "AI explanation settings were invalid.");
        }
    }

    private async Task ExplainCoreAsync()
    {
        string? logPath = await _dialogs.PickOpenMeasurementLogAsync();
        if (logPath == null)
        {
            return;
        }

        if (!File.Exists(logPath))
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Selected measurement log does not exist.");
            return;
        }

        if (!MeasurementLogExtensions.Contains(Path.GetExtension(logPath)))
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Select a measurement log file (.csv, .log, or .txt).");
            return;
        }

        var logFile = new FileInfo(logPath);
        if (logFile.Length > AiExplanationService.MaxLogFileBytes)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", LogTooLargeMessage());
            return;
        }

        string logText = await File.ReadAllTextAsync(logPath);
        if (string.IsNullOrWhiteSpace(logText))
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Selected measurement log is empty.");
            return;
        }

        if (logText.Length > AiExplanationService.MaxLogChars)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", LogTooLargeMessage());
            return;
        }

        bool credentialStoreAvailable = await _credentialStore.ProbeAsync(CancellationToken.None);
        AiBackendCredentials? savedCredentials = credentialStoreAvailable
            ? await _credentialStore.ReadAsync(CancellationToken.None)
            : null;

        var request = new AiExplanationDialogRequest(
            AiExplanationService.BackendOptions,
            AiExplanationService.PrimaryBackendBaseUrl,
            savedCredentials,
            credentialStoreAvailable);
        AiExplanationDialogResult? dialogResult = await _dialogs.AskAiExplanationAsync(request);
        if (dialogResult == null || !dialogResult.ConsentGranted)
        {
            return;
        }

        var credentials = new AiBackendCredentials(dialogResult.Username, dialogResult.Password);
        AiExplanationResult result = await _aiExplanationService.ExplainMeasurementLogAsync(
            dialogResult.BackendBaseUrl,
            logText,
            credentials,
            dialogResult.ConsentGranted,
            CancellationToken.None);

        string? credentialUpdateError = await UpdateCredentialStoreAfterSuccessAsync(
            dialogResult,
            credentials,
            credentialStoreAvailable);
        if (credentialUpdateError != null)
        {
            await _dialogs.ShowErrorAsync("AI Explanation", credentialUpdateError);
        }

        await _dialogs.ShowAiExplanationAsync(new AiExplanationDisplay(
            result.RequestId,
            result.Explanation,
            result.Model,
            AiExplanationService.NormalizeApprovedBackendBaseUrl(dialogResult.BackendBaseUrl)));
    }

    private async Task<string?> UpdateCredentialStoreAfterSuccessAsync(
        AiExplanationDialogResult dialogResult,
        AiBackendCredentials credentials,
        bool credentialStoreAvailable)
    {
        if (!credentialStoreAvailable)
        {
            return null;
        }

        try
        {
            if (dialogResult.RememberCredentials)
            {
                bool saved = await _credentialStore.SaveAsync(credentials, CancellationToken.None);
                return saved
                    ? null
                    : "Credentials could not be saved by the operating system credential store. This request completed without changing saved login state.";
            }

            await _credentialStore.DeleteAsync(CancellationToken.None);
            return null;
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

    private async Task ShowServiceErrorAsync(AiExplanationServiceException ex)
    {
        string requestIdText = string.IsNullOrWhiteSpace(ex.RequestId)
            ? string.Empty
            : $"\n\nRequest ID: {ex.RequestId}";
        await _dialogs.ShowErrorAsync("AI Explanation", ex.Message + requestIdText);
    }

    private static string LogTooLargeMessage() =>
        "Measurement log is too large. Select a log under " +
        AiExplanationService.MaxLogChars.ToString("N0") +
        " characters.";
}
