using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TimeGrapher.App.Services;

internal interface IAiExplanationService
{
    Task<AiExplanationResult> ExplainMeasurementLogAsync(
        string backendBaseUrl,
        string logText,
        AiBackendCredentials credentials,
        CancellationToken cancellationToken);
}

internal sealed class AiExplanationService : IAiExplanationService
{
    public const string PrimaryBackendBaseUrl = "https://tg-ai.jaehongoh.com";
    public const string AwsBackendBaseUrl = "https://tg-ai-cmu-aws.jaehongoh.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> ApprovedBackendBaseUrls = new(StringComparer.Ordinal)
    {
        PrimaryBackendBaseUrl,
        AwsBackendBaseUrl
    };

    private readonly HttpClient _httpClient;

    public AiExplanationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static IReadOnlyList<AiBackendOption> BackendOptions { get; } = new[]
    {
        new AiBackendOption("Primary VPS", PrimaryBackendBaseUrl),
        new AiBackendOption("AWS Learner Lab", AwsBackendBaseUrl)
    };

    public async Task<AiExplanationResult> ExplainMeasurementLogAsync(
        string backendBaseUrl,
        string logText,
        AiBackendCredentials credentials,
        CancellationToken cancellationToken)
    {
        string normalizedBaseUrl = NormalizeApprovedBackendBaseUrl(backendBaseUrl);
        var requestBody = new AiExplanationRequest(
            ConsentGranted: true,
            Locale: "ko-KR",
            AppVersion: AppVersionInfo.Current,
            LogText: logText);
        string json = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            normalizedBaseUrl + "/api/watch/explain-measurement-log");
        string pair = $"{credentials.Username}:{credentials.Password}";
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            AiExplanationResult? result = JsonSerializer.Deserialize<AiExplanationResult>(responseText, JsonOptions);
            if (result is { Explanation.Length: > 0 })
            {
                return result;
            }

            throw new AiExplanationServiceException(
                response.StatusCode,
                null,
                "invalid_success_response",
                "AI explanation response was invalid.");
        }

        AiExplanationError? error = TryParseError(responseText);
        throw new AiExplanationServiceException(
            response.StatusCode,
            error?.RequestId,
            error?.Error ?? "http_error",
            MapUserMessage(response.StatusCode, error?.Message));
    }

    public static string NormalizeApprovedBackendBaseUrl(string backendBaseUrl)
    {
        if (!Uri.TryCreate(backendBaseUrl, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("Backend URL must be absolute.", nameof(backendBaseUrl));
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')))
        {
            throw new ArgumentException("Backend URL must be an approved HTTPS base URL.", nameof(backendBaseUrl));
        }

        string normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (!ApprovedBackendBaseUrls.Contains(normalized))
        {
            throw new ArgumentException("Backend URL is not approved for production use.", nameof(backendBaseUrl));
        }

        return normalized;
    }

    private static AiExplanationError? TryParseError(string responseText)
    {
        try
        {
            return JsonSerializer.Deserialize<AiExplanationError>(responseText, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MapUserMessage(HttpStatusCode statusCode, string? backendMessage) => statusCode switch
    {
        HttpStatusCode.BadRequest => backendMessage ?? "Measurement log input was invalid.",
        HttpStatusCode.Unauthorized => "Demo username or password is incorrect.",
        HttpStatusCode.Forbidden => "Backend protection rejected the request. Check the API host's Cloudflare rules.",
        HttpStatusCode.RequestEntityTooLarge => "Measurement log is too large. Use a shorter log or smaller measurement window.",
        (HttpStatusCode)429 => "AI request limit was reached. Please retry later.",
        HttpStatusCode.BadGateway => "AI explanation is temporarily unavailable.",
        HttpStatusCode.ServiceUnavailable => "AI explanation is currently unavailable.",
        _ => backendMessage ?? "AI explanation request failed."
    };
}

internal sealed class AiExplanationServiceException : Exception
{
    public AiExplanationServiceException(HttpStatusCode statusCode, string? requestId, string error, string message)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
        Error = error;
    }

    public HttpStatusCode StatusCode { get; }

    public string? RequestId { get; }

    public string Error { get; }
}

internal static class AppVersionInfo
{
    public static string Current => typeof(AppVersionInfo).Assembly.GetName().Version?.ToString() ?? "unknown";
}
