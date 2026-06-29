using TimeGrapher.App.ViewModels;

namespace TimeGrapher.App.Services;

internal sealed class AiExplanationController : IAiExplanationRunner
{
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

        string logText = await File.ReadAllTextAsync(logPath);
        if (string.IsNullOrWhiteSpace(logText))
        {
            await _dialogs.ShowErrorAsync("AI Explanation", "Selected measurement log is empty.");
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
        if (dialogResult.RememberCredentials && credentialStoreAvailable)
        {
            bool saved = await _credentialStore.SaveAsync(credentials, CancellationToken.None);
            if (!saved)
            {
                await _dialogs.ShowErrorAsync("AI Explanation", "Credentials could not be saved by the operating system credential store. This request will continue without changing saved login state.");
            }
        }
        else if (credentialStoreAvailable)
        {
            await _credentialStore.DeleteAsync(CancellationToken.None);
        }

        try
        {
            AiExplanationResult result = await _aiExplanationService.ExplainMeasurementLogAsync(
                dialogResult.BackendBaseUrl,
                logText,
                credentials,
                CancellationToken.None);
            await _dialogs.ShowAiExplanationAsync(new AiExplanationDisplay(
                result.RequestId,
                result.Explanation,
                result.Model,
                AiExplanationService.NormalizeApprovedBackendBaseUrl(dialogResult.BackendBaseUrl)));
        }
        catch (AiExplanationServiceException ex)
        {
            string requestIdText = string.IsNullOrWhiteSpace(ex.RequestId)
                ? string.Empty
                : $"\n\nRequest ID: {ex.RequestId}";
            await _dialogs.ShowErrorAsync("AI Explanation", ex.Message + requestIdText);
        }
    }
}
