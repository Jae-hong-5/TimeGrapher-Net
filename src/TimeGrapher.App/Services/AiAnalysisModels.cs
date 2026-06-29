namespace TimeGrapher.App.Services;

internal sealed record AiBackendOption(string DisplayName, string BaseUrl);

internal sealed record AiBackendCredentials(string Username, string Password);

internal sealed record AiAnalysisDialogRequest(
    IReadOnlyList<AiBackendOption> BackendOptions,
    string SelectedBackendBaseUrl,
    AiBackendCredentials? SavedCredentials,
    bool CredentialPersistenceAvailable);

internal sealed record AiAnalysisDialogResult(
    string BackendBaseUrl,
    string Username,
    string Password,
    bool RememberCredentials,
    bool ConsentGranted);

internal sealed record AiAnalysisResult(string RequestId, string Explanation, string Model);

internal sealed record AiAnalysisError(string? RequestId, string Error, string Message);

internal sealed record AiAnalysisDisplay(string RequestId, string Explanation, string Model, string BackendBaseUrl);
internal sealed record AiAnalysisProgressDisplay(string BackendBaseUrl, string StatusText);

internal sealed record AiAnalysisFailureDisplay(string Message, string? RequestId);

internal sealed record AiAnalysisRequest(
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
