using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TimeGrapher.App.Services;

internal interface IAiAnalysisService
{
    Task<AiAnalysisResult> AnalyzeMeasurementLogAsync(
        string backendBaseUrl,
        string logText,
        AiBackendCredentials credentials,
        bool consentGranted,
        CancellationToken cancellationToken);
}

internal sealed class AiAnalysisService : IAiAnalysisService
{
    public const string PrimaryBackendBaseUrl = "https://tg-ai.jaehongoh.com";
    public const string AwsBackendBaseUrl = "https://tg-ai-cmu-aws.jaehongoh.com";
    public const int MaxLogChars = 90_000;
    public const int MaxLogFileBytes = MaxLogChars * 4 + 4096;
    public const int MaxResponseChars = 64_000;

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

    public AiAnalysisService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static IReadOnlyList<AiBackendOption> BackendOptions { get; } = new[]
    {
        new AiBackendOption("TimeGrapher Service Server (KR)", PrimaryBackendBaseUrl),
        new AiBackendOption("AWS Learner Lab EC2 Server (US-EAST)", AwsBackendBaseUrl)
    };

    public async Task<AiAnalysisResult> AnalyzeMeasurementLogAsync(
        string backendBaseUrl,
        string logText,
        AiBackendCredentials credentials,
        bool consentGranted,
        CancellationToken cancellationToken)
    {
        if (!consentGranted)
        {
            throw new AiAnalysisServiceException(
                HttpStatusCode.BadRequest,
                null,
                "missing_consent",
                "Consent is required before uploading the measurement log.");
        }

        if (logText.Length > MaxLogChars)
        {
            throw new AiAnalysisServiceException(
                HttpStatusCode.RequestEntityTooLarge,
                null,
                "log_too_large",
                "Measurement log is too large. Use a shorter log or smaller measurement window.");
        }

        string normalizedBaseUrl = NormalizeApprovedBackendBaseUrl(backendBaseUrl);
        var requestBody = new AiAnalysisRequest(
            ConsentGranted: consentGranted,
            Locale: "en-US",
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

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiAnalysisServiceException(
                HttpStatusCode.GatewayTimeout,
                null,
                "request_timeout",
                "AI analysis request timed out. Retry with the same log or try the other server.");
        }
        catch (HttpRequestException)
        {
            throw new AiAnalysisServiceException(
                0,
                null,
                "transport_error",
                "AI analysis server could not be reached. Check the network or try the other server.");
        }

        using (response)
        {
            string responseText;
            try
            {
                responseText = await ReadResponseTextAsync(response, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiAnalysisServiceException(
                    HttpStatusCode.GatewayTimeout,
                    null,
                    "request_timeout",
                    "AI analysis request timed out. Retry with the same log or try the other server.");
            }
            catch (IOException)
            {
                throw new AiAnalysisServiceException(
                    0,
                    null,
                    "transport_error",
                    "AI analysis server could not be reached. Check the network or try the other server.");
            }

            if (response.IsSuccessStatusCode)
            {
                AiAnalysisResult? result;
                try
                {
                    result = JsonSerializer.Deserialize<AiAnalysisResult>(responseText, JsonOptions);
                }
                catch (JsonException)
                {
                    result = null;
                }

                if (result is { Explanation.Length: > 0 })
                {
                    return result;
                }

                throw new AiAnalysisServiceException(
                    response.StatusCode,
                    null,
                    "invalid_success_response",
                    "AI analysis response was invalid.");
            }

            AiAnalysisError? error = TryParseError(responseText);
            throw new AiAnalysisServiceException(
                response.StatusCode,
                error?.RequestId,
                error?.Error ?? "http_error",
                MapUserMessage(response.StatusCode, error?.Message));
        }
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

    private static AiAnalysisError? TryParseError(string responseText)
    {
        try
        {
            return JsonSerializer.Deserialize<AiAnalysisError>(responseText, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ReadResponseTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        var builder = new StringBuilder();
        char[] buffer = new char[4096];
        while (true)
        {
            int remaining = MaxResponseChars + 1 - builder.Length;
            if (remaining <= 0)
            {
                throw ResponseTooLarge(response.StatusCode);
            }

            int read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                return builder.ToString();
            }

            builder.Append(buffer, 0, read);
            if (builder.Length > MaxResponseChars)
            {
                throw ResponseTooLarge(response.StatusCode);
            }
        }
    }

    private static AiAnalysisServiceException ResponseTooLarge(HttpStatusCode statusCode) => new(
        statusCode,
        null,
        "response_too_large",
        "AI analysis response was too large.");

    private static string MapUserMessage(HttpStatusCode statusCode, string? backendMessage) => statusCode switch
    {
        HttpStatusCode.BadRequest => backendMessage ?? "Measurement log input was invalid.",
        HttpStatusCode.Unauthorized => "User ID or User PW is incorrect.",
        HttpStatusCode.Forbidden => "Server protection rejected the request. Try the other server.",
        HttpStatusCode.RequestEntityTooLarge => "Measurement log is too large. Use a shorter log or smaller measurement window.",
        (HttpStatusCode)429 => "AI request limit was reached. Please retry later.",
        HttpStatusCode.BadGateway => "AI analysis is temporarily unavailable.",
        HttpStatusCode.GatewayTimeout => "AI analysis timed out upstream. Retry with the same log or try the other server.",
        HttpStatusCode.ServiceUnavailable => "AI analysis is currently unavailable.",
        _ => backendMessage ?? "AI analysis request failed."
    };
}

internal sealed class AiAnalysisServiceException : Exception
{
    public AiAnalysisServiceException(HttpStatusCode statusCode, string? requestId, string error, string message)
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
