using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AiAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeMeasurementLogAsync_SendsApprovedBackendRequestWithBasicAuth()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        using var client = new HttpClient(new CapturingHandler(async request =>
        {
            captured = request;
            capturedContent = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, "{\"requestId\":\"rid\",\"explanation\":\"설명\",\"model\":\"gemini-test\"}");
        }));
        var service = new AiAnalysisService(client);

        AiAnalysisResult result = await service.AnalyzeMeasurementLogAsync(
            AiAnalysisService.AwsBackendBaseUrl,
            "rate_valid,rate_s_per_day\ntrue,3.2",
            new AiBackendCredentials("grader", "secret"),
            consentGranted: true,
            CancellationToken.None);

        Assert.Equal("설명", result.Explanation);
        Assert.Equal("gemini-test", result.Model);
        Assert.NotNull(captured);
        Assert.Equal("https://tg-ai-cmu-aws.jaehongoh.com/api/watch/explain-measurement-log", captured!.RequestUri!.ToString());
        Assert.Equal("Basic", captured.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("grader:secret")), captured.Headers.Authorization?.Parameter);
        Assert.Contains("\"consentGranted\":true", capturedContent);
        Assert.Contains("\"locale\":\"ko-KR\"", capturedContent);
        Assert.Contains("\"logText\":\"rate_valid,rate_s_per_day\\ntrue,3.2\"", capturedContent);
    }

    [Fact]
    public void BackendOptions_UseReadableServerLabelsWithoutDomains()
    {
        Assert.Equal(
            new[]
            {
                "TimeGrapher Service Server (KR)",
                "AWS Learner Lab EC2 Server (US-EAST)",
            },
            AiAnalysisService.BackendOptions.Select(option => option.DisplayName).ToArray());
        Assert.DoesNotContain(AiAnalysisService.BackendOptions, option => option.DisplayName.Contains("http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(AiAnalysisService.BackendOptions, option => option.DisplayName.Contains("jaehongoh.com", StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData("http://tg-ai.jaehongoh.com")]
    [InlineData("https://example.com")]
    [InlineData("https://tg-ai.jaehongoh.com/api/watch/explain-measurement-log")]
    public void NormalizeApprovedBackendBaseUrl_RejectsUnapprovedUrls(string backendBaseUrl)
    {
        Assert.Throws<ArgumentException>(() => AiAnalysisService.NormalizeApprovedBackendBaseUrl(backendBaseUrl));
    }

    [Fact]
    public void NormalizeApprovedBackendBaseUrl_AcceptsApprovedUrls_AsCanonicalSlashFree()
    {
        Assert.Equal(
            AiAnalysisService.PrimaryBackendBaseUrl,
            AiAnalysisService.NormalizeApprovedBackendBaseUrl(AiAnalysisService.PrimaryBackendBaseUrl));
        Assert.Equal(
            AiAnalysisService.AwsBackendBaseUrl,
            AiAnalysisService.NormalizeApprovedBackendBaseUrl(AiAnalysisService.AwsBackendBaseUrl));
    }

    [Fact]
    public void NormalizeApprovedBackendBaseUrl_TrimsTrailingSlash_ToTheSameCanonicalValue()
    {
        Assert.Equal(
            AiAnalysisService.PrimaryBackendBaseUrl,
            AiAnalysisService.NormalizeApprovedBackendBaseUrl(AiAnalysisService.PrimaryBackendBaseUrl + "/"));
        Assert.Equal(
            AiAnalysisService.AwsBackendBaseUrl,
            AiAnalysisService.NormalizeApprovedBackendBaseUrl(AiAnalysisService.AwsBackendBaseUrl + "/"));
    }

    [Theory]
    [InlineData(400, "bad", "backend message")]
    [InlineData(401, "unauthorized", "User ID or User PW is incorrect.")]
    [InlineData(403, "forbidden", "Server protection rejected the request.")]
    [InlineData(413, "log_too_large", "Measurement log is too large.")]
    [InlineData(429, "rate_limit_minute", "AI request limit was reached.")]
    [InlineData(502, "gemini_upstream_failed", "AI analysis is temporarily unavailable.")]
    [InlineData(503, "ai_disabled", "AI analysis is currently unavailable.")]
    public async Task AnalyzeMeasurementLogAsync_MapsBackendErrors(int statusCode, string errorCode, string expectedMessagePrefix)
    {
        using var client = new HttpClient(new CapturingHandler(_ => Task.FromResult(
            JsonResponse((HttpStatusCode)statusCode, $"{{\"requestId\":\"rid\",\"error\":\"{errorCode}\",\"message\":\"backend message\"}}"))));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                consentGranted: true,
                CancellationToken.None));

        Assert.Equal((HttpStatusCode)statusCode, ex.StatusCode);
        Assert.Equal("rid", ex.RequestId);
        Assert.Equal(errorCode, ex.Error);
        Assert.StartsWith(expectedMessagePrefix, ex.Message);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_RejectsMissingConsentBeforeSending()
    {
        using var client = new HttpClient(new CapturingHandler(_ => throw new InvalidOperationException("Should not send")));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                consentGranted: false,
                CancellationToken.None));

        Assert.Equal("missing_consent", ex.Error);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_RejectsOversizedLogBeforeSending()
    {
        using var client = new HttpClient(new CapturingHandler(_ => throw new InvalidOperationException("Should not send")));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                new string('x', AiAnalysisService.MaxLogChars + 1),
                new AiBackendCredentials("grader", "secret"),
                consentGranted: true,
                CancellationToken.None));

        Assert.Equal("log_too_large", ex.Error);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_AcceptsLogAtExactlyMaxLogChars()
    {
        // The size gate rejects MaxLogChars+1 (above) but must ACCEPT exactly MaxLogChars:
        // a request goes out and a success result comes back.
        HttpRequestMessage? captured = null;
        using var client = new HttpClient(new CapturingHandler(request =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK, "{\"requestId\":\"rid\",\"explanation\":\"ok\",\"model\":\"gemini-test\"}"));
        }));
        var service = new AiAnalysisService(client);

        AiAnalysisResult result = await service.AnalyzeMeasurementLogAsync(
            AiAnalysisService.PrimaryBackendBaseUrl,
            new string('x', AiAnalysisService.MaxLogChars),
            new AiBackendCredentials("grader", "secret"),
            consentGranted: true,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("ok", result.Explanation);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_MapsTransportFailure()
    {
        using var client = new HttpClient(new CapturingHandler(_ => throw new HttpRequestException("offline")));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                consentGranted: true,
                CancellationToken.None));

        Assert.Equal("transport_error", ex.Error);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_MapsInvalidSuccessJson()
    {
        using var client = new HttpClient(new CapturingHandler(_ => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, "not-json"))));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                consentGranted: true,
                CancellationToken.None));

        Assert.Equal("invalid_success_response", ex.Error);
    }

    [Fact]
    public async Task AnalyzeMeasurementLogAsync_RejectsOversizedResponse()
    {
        using var client = new HttpClient(new CapturingHandler(_ => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, new string('x', AiAnalysisService.MaxResponseChars + 1)))));
        var service = new AiAnalysisService(client);

        AiAnalysisServiceException ex = await Assert.ThrowsAsync<AiAnalysisServiceException>(() =>
            service.AnalyzeMeasurementLogAsync(
                AiAnalysisService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                consentGranted: true,
                CancellationToken.None));

        Assert.Equal("response_too_large", ex.Error);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;

        public CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }
}
