using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TimeGrapher.App.Services;
using Xunit;

namespace TimeGrapher.App.Tests;

public sealed class AiExplanationServiceTests
{
    [Fact]
    public async Task ExplainMeasurementLogAsync_SendsApprovedBackendRequestWithBasicAuth()
    {
        HttpRequestMessage? captured = null;
        string? capturedContent = null;
        using var client = new HttpClient(new CapturingHandler(async request =>
        {
            captured = request;
            capturedContent = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, "{\"requestId\":\"rid\",\"explanation\":\"설명\",\"model\":\"gemini-test\"}");
        }));
        var service = new AiExplanationService(client);

        AiExplanationResult result = await service.ExplainMeasurementLogAsync(
            AiExplanationService.AwsBackendBaseUrl,
            "rate_valid,rate_s_per_day\ntrue,3.2",
            new AiBackendCredentials("grader", "secret"),
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

    [Theory]
    [InlineData("http://tg-ai.jaehongoh.com")]
    [InlineData("https://example.com")]
    [InlineData("https://tg-ai.jaehongoh.com/api/watch/explain-measurement-log")]
    public void NormalizeApprovedBackendBaseUrl_RejectsUnapprovedUrls(string backendBaseUrl)
    {
        Assert.Throws<ArgumentException>(() => AiExplanationService.NormalizeApprovedBackendBaseUrl(backendBaseUrl));
    }

    [Theory]
    [InlineData(400, "bad", "backend message")]
    [InlineData(401, "unauthorized", "Demo username or password is incorrect.")]
    [InlineData(403, "forbidden", "Backend protection rejected the request.")]
    [InlineData(413, "log_too_large", "Measurement log is too large.")]
    [InlineData(429, "rate_limit_minute", "AI request limit was reached.")]
    [InlineData(502, "gemini_upstream_failed", "AI explanation is temporarily unavailable.")]
    [InlineData(503, "ai_disabled", "AI explanation is currently unavailable.")]
    public async Task ExplainMeasurementLogAsync_MapsBackendErrors(int statusCode, string errorCode, string expectedMessagePrefix)
    {
        using var client = new HttpClient(new CapturingHandler(_ => Task.FromResult(
            JsonResponse((HttpStatusCode)statusCode, $"{{\"requestId\":\"rid\",\"error\":\"{errorCode}\",\"message\":\"backend message\"}}"))));
        var service = new AiExplanationService(client);

        AiExplanationServiceException ex = await Assert.ThrowsAsync<AiExplanationServiceException>(() =>
            service.ExplainMeasurementLogAsync(
                AiExplanationService.PrimaryBackendBaseUrl,
                "log",
                new AiBackendCredentials("grader", "secret"),
                CancellationToken.None));

        Assert.Equal((HttpStatusCode)statusCode, ex.StatusCode);
        Assert.Equal("rid", ex.RequestId);
        Assert.Equal(errorCode, ex.Error);
        Assert.StartsWith(expectedMessagePrefix, ex.Message);
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
