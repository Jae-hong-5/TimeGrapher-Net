namespace TimeGrapher.App.Services;

internal sealed record AiBackendOption(string DisplayName, string BaseUrl);

internal sealed record AiBackendCredentials(string Username, string Password);

internal sealed record AiExplanationDialogRequest(
    IReadOnlyList<AiBackendOption> BackendOptions,
    string SelectedBackendBaseUrl,
    AiBackendCredentials? SavedCredentials,
    bool CredentialPersistenceAvailable);

internal sealed record AiExplanationDialogResult(
    string BackendBaseUrl,
    string Username,
    string Password,
    bool RememberCredentials,
    bool ConsentGranted);

internal sealed record AiExplanationResult(string RequestId, string Explanation, string Model);

internal sealed record AiExplanationError(string? RequestId, string Error, string Message);

internal sealed record AiExplanationDisplay(string RequestId, string Explanation, string Model, string BackendBaseUrl);
internal sealed record AiExplanationProgressDisplay(string BackendBaseUrl, string StatusText);

internal sealed record AiExplanationFailureDisplay(string Message, string? RequestId);

internal sealed record AiExplanationRequest(
    bool ConsentGranted,
    string Locale,
    string AppVersion,
    string LogText,
    MeasurementSummary? MeasurementSummary = null);

internal sealed record MeasurementSummary(
    int? Bph,
    double? RateSecondsPerDay,
    double? BeatErrorMs,
    double? AmplitudeDegrees,
    double? Confidence);
