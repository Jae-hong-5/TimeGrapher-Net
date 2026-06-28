# TimeGrapher App Gemini Backend Integration Guide

Status: ready for app-side implementation.
Backend base URL: `https://tg-ai.jaehongoh.com`

This document is for the coding agent implementing the TimeGrapher app-side
integration. The private backend is already deployed and is responsible for
holding the project-owned Gemini API key, building the fixed prompt, calling
Gemini, and returning only the generated explanation.

## 1. Required App Behavior

The app must use the private backend for Gemini-powered explanations.

```text
TimeGrapher App
  -> HTTPS POST with Basic Auth, user consent, and logText
Private Backend
  -> validates auth, consent, size/rate limits
  -> builds server-owned prompt
  -> calls Gemini with server-side API key
  -> returns explanation text
TimeGrapher App
  -> displays explanation
```

The app must not call Gemini directly.

The app must not accept, store, or use a Gemini API key.

The app must not send arbitrary prompts to the backend. Send measurement data
only.

## 2. Public Backend Contract

### Health Check

```http
GET https://tg-ai.jaehongoh.com/health
```

Expected success:

```json
{
  "status": "ok"
}
```

Use this only for diagnostics or optional connection testing. Do not expose
backend internals in the UI.

### AI Explanation Endpoint

```http
POST https://tg-ai.jaehongoh.com/api/watch/explain-measurement-log
Authorization: Basic <base64(username:password)>
Content-Type: application/json
```

Minimum request body:

```json
{
  "consentGranted": true,
  "locale": "ko-KR",
  "appVersion": "1.0.0",
  "logText": "full TimeGrapher CSV/log text"
}
```

Optional request body with structured summary:

```json
{
  "consentGranted": true,
  "locale": "ko-KR",
  "appVersion": "1.0.0",
  "logText": "full TimeGrapher CSV/log text",
  "measurementSummary": {
    "bph": 28800,
    "rateSecondsPerDay": 3.2,
    "beatErrorMs": 0.4,
    "amplitudeDegrees": 270,
    "confidence": 0.91
  }
}
```

For the first app implementation, `measurementSummary` may be omitted. The
backend accepts a full CSV/log in `logText`.

Success response:

```json
{
  "requestId": "dc6f3f31-79c9-48d3-8efb-022adef65349",
  "explanation": "Korean explanation text",
  "model": "gemini-3.5-flash"
}
```

Error response:

```json
{
  "requestId": "generated-request-id",
  "error": "stable_error_code",
  "message": "safe user-facing message"
}
```

## 3. Credentials

The app should ask the grader/user for:

- demo username
- demo password

These credentials are provided privately outside the app package.

Do not hardcode demo credentials.

Do not commit demo credentials.

Do not log credentials.

Do not include credentials in screenshots, telemetry, crash reports, or support
logs.

For the first implementation, keeping credentials in memory for the current app
session is enough. If persistence is later required, use the operating system
credential store. Do not save credentials in a plain text config file.

Basic Auth construction:

```text
base64(UTF8(username + ":" + password))
```

Header:

```http
Authorization: Basic <encoded-value>
```

## 4. Consent Flow

Before calling the AI endpoint, the app must clearly ask for upload consent.

The consent text should state that the selected TimeGrapher analysis log will be
sent to the private backend for AI explanation.

Only send:

```json
"consentGranted": true
```

after the user explicitly agrees.

If the user cancels or declines, do not call the backend.

Suggested UI flow:

1. User records or selects a measurement log.
2. User clicks an AI explanation action.
3. App shows a consent dialog.
4. User enters or confirms demo credentials.
5. App sends `logText` to the backend.
6. App displays `explanation`.

## 5. Log Upload

Send the whole CSV/log text as `logText`.

The backend currently treats `logText` as untrusted measurement data and wraps it
inside a fixed server-side prompt. The backend prompt instructs Gemini to:

- parse the timegrapher log conservatively
- exclude truncated rows or rows with mismatched column counts
- use only values whose corresponding `*_valid` flag is true
- evaluate `beat_error_ms` mostly by absolute value
- treat short-term sign flips or large jumps as possible measurement artifacts
- show original and robust statistics instead of silently deleting outliers
- check `missed_beat_detections` and `sync_loss_count`
- make conservative conclusions for short, single-position measurements
- output in Korean

The app should not duplicate this prompt and should not send prompt text.

## 6. Size and Rate Limits

The backend enforces request limits before calling Gemini.

The exact deployed values are server-configurable, so the app should handle
these errors gracefully:

- `413 Payload Too Large`: log or JSON body is too large
- `429 Too Many Requests`: per-client or global quota exceeded

Current intended deployment settings are expected to support ordinary CSV logs,
including logs around 25 KB. If a log is too large, show a friendly message and
ask the user to shorten the log or retry with a smaller measurement window.

Do not split one analysis into many backend calls unless the backend contract is
explicitly changed later.

## 7. Status Code Handling

Handle these responses:

```text
200 OK
  Display response.explanation.

400 Bad Request
  Missing consent, missing log, invalid JSON, or invalid input.
  Show a user-facing validation error.

401 Unauthorized
  Missing or wrong demo credentials.
  Ask the user to re-enter credentials.

413 Payload Too Large
  Log/body exceeds backend limits.
  Ask the user to use a smaller log.

429 Too Many Requests
  Rate limit or quota exceeded.
  Ask the user to retry later.

502 Bad Gateway
  Gemini upstream failed.
  Show that AI explanation is temporarily unavailable.

503 Service Unavailable
  AI feature disabled or backend not configured.
  Show that AI explanation is currently unavailable.
```

Always preserve `requestId` from error responses when showing advanced details
or when the user reports a problem. Do not show raw credentials or uploaded log
content in error dialogs.

## 8. Recommended App Service Boundary

Keep backend integration behind an app-facing service, for example:

```text
IAiExplanationService
  ExplainMeasurementLogAsync(logText, credentials, consentGranted, cancellationToken)
```

UI code should not know Gemini protocol details. UI code should only know that
it asks an app service for an explanation.

Suggested service responsibilities:

- build the backend JSON request
- add Basic Auth
- set `Content-Type: application/json`
- send HTTPS request
- parse success and error responses
- map backend errors to app UI states
- avoid logging sensitive values

`TimeGrapher.Core` should not depend on HTTP clients, Gemini, credentials, or UI.

## 9. C#-Style DTO Sketch

Use the app's existing coding style, but keep the wire names compatible with the
backend.

```csharp
public sealed record AiExplanationRequest(
    bool ConsentGranted,
    string Locale,
    string AppVersion,
    string LogText,
    MeasurementSummary? MeasurementSummary = null);

public sealed record MeasurementSummary(
    int? Bph,
    double? RateSecondsPerDay,
    double? BeatErrorMs,
    double? AmplitudeDegrees,
    double? Confidence);

public sealed record AiExplanationResponse(
    string RequestId,
    string Explanation,
    string Model);

public sealed record AiErrorResponse(
    string RequestId,
    string Error,
    string Message);
```

If using `System.Text.Json`, make sure JSON property naming is camelCase:

```json
consentGranted
locale
appVersion
logText
measurementSummary
```

## 10. C#-Style Request Sketch

This is illustrative. Adapt it to the app's existing architecture.

```csharp
var requestBody = new
{
    consentGranted = true,
    locale = "ko-KR",
    appVersion = appVersion,
    logText = csvLogText
};

var json = JsonSerializer.Serialize(requestBody);

using var request = new HttpRequestMessage(
    HttpMethod.Post,
    "https://tg-ai.jaehongoh.com/api/watch/explain-measurement-log");

var pair = $"{username}:{password}";
var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));

request.Headers.Authorization =
    new AuthenticationHeaderValue("Basic", encoded);

request.Content = new StringContent(json, Encoding.UTF8, "application/json");

using var response = await httpClient.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);

var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
```

On `2xx`, parse `AiExplanationResponse` and display `Explanation`.

On non-`2xx`, parse `AiErrorResponse` if possible and map it to the status code
handling rules above.

## 11. UI Requirements

Add or update UI for:

- backend-powered AI explanation action
- consent confirmation before upload
- demo username/password input
- loading state while waiting for the backend
- cancellation support if the app already supports cancellable operations
- success display for Korean explanation text
- retry-friendly error display

Avoid showing technical backend details by default. A small advanced/details
area may show `requestId` for support.

## 12. Security Checklist for the App Agent

Before considering the app-side task done, verify:

- No Gemini API key exists in app source, assets, config, README, or installer.
- No demo username/password exists in app source, assets, config, README, or installer.
- App does not expose BYOK or direct Gemini access.
- App calls only `https://tg-ai.jaehongoh.com/api/watch/explain-measurement-log`.
- App sends `consentGranted=true` only after explicit user consent.
- App sends log data as `logText`, not as prompt instructions.
- App handles `401`, `413`, `429`, `502`, and `503`.
- App does not log Basic Auth headers, passwords, or full uploaded logs.
- App displays the backend `explanation` on success.

## 13. Manual Test Cases

Use these after implementing the app integration:

1. Health check succeeds.
2. AI explanation without credentials fails with `401`.
3. AI explanation with wrong credentials fails with `401`.
4. Declining consent results in no backend request.
5. Empty log is blocked by the app or returns `400`.
6. Normal CSV log returns `200` and displays Korean explanation.
7. Oversized log shows a friendly too-large message.
8. Repeated rapid requests eventually show a retry-later message.

The backend has already been smoke-tested successfully with real Gemini:

```text
HTTP 200
response contains requestId, explanation, and model
```
