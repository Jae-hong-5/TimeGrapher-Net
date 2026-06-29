# TimeGrapher App Gemini Backend Integration Guide

Status: implemented on the app side; backend contract is ready and the remaining
work is operational validation against deployed backend credentials.
Allowed backend base URLs:

```text
Primary:
https://tg-ai.jaehongoh.com

AWS Learner Lab:
https://tg-ai-cmu-aws.jaehongoh.com
```

The app should keep the backend base URL configurable. Use the primary backend
by default and allow switching to the AWS Learner Lab backend for demos or
fallback testing.

The backend base URL is not a secret, but production builds must constrain it
to the approved HTTPS allowlist above. Do not let ordinary users type an
arbitrary AI backend URL unless the app has a clearly separated developer/debug
mode.

Automatic fallback is allowed only between the two approved URLs. If fallback
is implemented, make the chosen backend visible in diagnostics and avoid
repeated retry loops that could multiply Gemini calls.

This document records the TimeGrapher app-side integration. The private backend
is already deployed and is responsible for holding the project-owned Gemini API
key, building the fixed prompt, calling Gemini, and returning only the generated
explanation.

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
GET <backend-base-url>/health
```

Expected success:

```json
{
  "status": "ok"
}
```

Use this only for diagnostics or optional connection testing. Do not expose
backend internals in the UI.

### AI Analysis Endpoint

```http
POST <backend-base-url>/api/watch/explain-measurement-log
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

The app should support a "remember login" / auto-login option, but only through
the operating system credential store. Keeping credentials in memory is
acceptable when the user does not opt in to saving them or when the credential
store probe fails. Do not save credentials in `AppSettings`, plain text config
files, logs, screenshots, crash reports, or bundled assets.

The app should update saved credentials only after the backend request succeeds.
This prevents a failed `401` attempt from replacing a known-good saved login and
prevents an unverified login attempt from deleting the previous saved state.

Persistent-login targets for the first implementation:

- Windows: use the OS credential store.
- Raspberry Pi/Linux: use a Secret Service-compatible keyring such as GNOME
  Keyring or KWallet.
- Enable persistence only after a store/read/delete probe succeeds.
- If the probe fails, keep the app usable with in-memory credentials only and
  disable the remember-login option.

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
sent to the private backend for AI analysis.

Only send:

```json
"consentGranted": true
```

after the user explicitly agrees.

If the user cancels or declines, do not call the backend.

Suggested UI flow:

1. User records or selects a measurement log.
2. User clicks an AI analysis action.
3. App shows a consent dialog.
4. User enters or confirms demo credentials.
5. App opens the AI Analysis result window immediately, shows request status,
   and sends `logText` to the backend.
6. App updates that same window with `explanation` or the mapped backend error.

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

For the first app implementation, the log source must be explicit and
testable. Either read a user-selected CSV/log file or expose a narrow
`MeasurementLogController`/service seam for the latest completed measurement
log. Do not read a file that the logger is still writing unless the sink has
been closed or flushed. If no completed log is available, show a friendly
message instead of calling the backend.

## 6. Size and Rate Limits

The backend enforces request limits before calling Gemini.

The exact deployed values are server-configurable, so the app should handle
these errors gracefully:

- `413 Payload Too Large`: log or JSON body is too large
- `429 Too Many Requests`: per-client or global quota exceeded

Current deployment settings are expected to support ordinary full CSV logs,
including the 24 KB backend sample fixture. If a log is too large, show a
friendly message and ask the user to shorten the log or retry with a smaller
measurement window.

The deployed backend currently uses `MAX_LOG_CHARS=100000`. The app keeps a
client-side margin by limiting selected logs to 90,000 characters before sending
and rejecting obviously oversized files before reading them into memory.

The app also bounds response reading and display rendering so a backend or
network problem cannot create unbounded UI work.

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

403 Forbidden
  If the response is HTML or includes a Cloudflare challenge header, treat this
  as backend protection misconfiguration, not as wrong credentials.

413 Payload Too Large
  Log/body exceeds backend limits.
  Ask the user to use a smaller log.

429 Too Many Requests
  Rate limit or quota exceeded.
  Ask the user to retry later.

502 Bad Gateway
  Gemini upstream failed.
  Show that AI analysis is temporarily unavailable.

503 Service Unavailable
  AI feature disabled or backend not configured.
  Show that AI analysis is currently unavailable.
```

Always preserve `requestId` from JSON error responses when showing advanced
details or when the user reports a problem. Some infrastructure errors, such as
Cloudflare challenge HTML, will not include a backend `requestId`. Do not show
raw credentials or uploaded log content in error dialogs.

## 8. Recommended App Service Boundary

Keep backend integration behind an app-facing service, for example:

```text
IAiAnalysisService
  AnalyzeMeasurementLogAsync(logText, credentials, consentGranted, cancellationToken)
```

UI code should not know Gemini protocol details. UI code should only know that
it asks an app service for an explanation.

Suggested service responsibilities:

- validate the backend base URL against the approved allowlist
- build the backend JSON request
- add Basic Auth
- set `Content-Type: application/json`
- send HTTPS request
- parse success and error responses
- map backend errors to app UI states
- avoid logging sensitive values

`TimeGrapher.Core` should not depend on HTTP clients, Gemini, credentials, or UI.

For the current app architecture, place protocol code under
`src/TimeGrapher.App/Services` and keep Avalonia dialogs/views under
`src/TimeGrapher.App/Views`. The `MainWindowBootstrapper` composition root
should construct the AI service and credential adapter, while
`MainWindowViewModel` owns user-visible command state. This preserves the
existing MVVM boundary documented in `docs/for-ai/MODULE_USES_VIEW.md`.

## 9. C#-Style DTO Sketch

Use the app's existing coding style, but keep the wire names compatible with the
backend.

```csharp
public sealed record AiAnalysisRequest(
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

public sealed record AiAnalysisResponse(
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
    $"{backendBaseUrl.TrimEnd('/')}/api/watch/explain-measurement-log");

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

On `2xx`, parse `AiAnalysisResponse` and display `Explanation`.

On non-`2xx`, parse `AiErrorResponse` if possible and map it to the status code
handling rules above.

## 11. UI Requirements

Add or update UI for:

- approved backend selection for primary/AWS demo fallback
- backend-powered AI analysis action
- consent confirmation before upload
- demo username/password input
- remember-login checkbox backed only by the OS credential store
- disabled remember-login UI when the credential-store probe fails
- duplicate-request guard while waiting for the backend
- visible request-status display opened before the backend response arrives
- success display for Korean explanation text in the same AI Analysis window
- retry-friendly error display

Avoid showing technical backend details by default. A small advanced/details
area may show `requestId` for support.

## 12. Security Checklist for the App Agent

Before considering the app-side task done, verify:

- No Gemini API key exists in app source, assets, config, README, or installer.
- No demo username/password exists in app source, assets, config, README, or installer.
- App does not expose BYOK or direct Gemini access.
- App calls only approved backend base URLs:
  `https://tg-ai.jaehongoh.com` or `https://tg-ai-cmu-aws.jaehongoh.com`.
- App rejects non-HTTPS backend URLs in production builds.
- App sends `consentGranted=true` only after explicit user consent.
- App sends log data as `logText`, not as prompt instructions.
- App rejects logs above 90,000 characters before upload.
- App handles `400`, `401`, `403`, `413`, `429`, `502`, and `503`.
- App does not log Basic Auth headers, passwords, or full uploaded logs.
- App stores saved demo credentials only in the OS credential store.
- App updates saved credentials only after a successful backend response.
- App disables credential persistence when the store/read/delete probe fails.
- App does not put credentials into `AppSettings` or any plain text file.
- App displays the backend `explanation` on success.

## 13. Manual Test Cases

Use these after implementing the app integration:

1. Health check succeeds.
2. AI analysis without credentials fails with `401`.
3. AI analysis with wrong credentials fails with `401`.
4. Declining consent results in no backend request.
5. Credential-store probe failure disables remember-login and still allows an in-memory login.
6. Successful credential-store probe can save, load, and delete demo credentials without touching `AppSettings`.
7. Empty log is blocked by the app or returns `400`.
8. Normal CSV log returns `200` and displays Korean explanation.
9. Oversized log shows a friendly too-large message.
10. Repeated rapid requests eventually show a retry-later message.
11. Both approved backend URLs can be selected and tested.
12. A Cloudflare `403` challenge response is shown as a backend protection issue, not as an auth failure.

The backend has already been smoke-tested successfully with real Gemini:

```text
HTTP 200
response contains requestId, explanation, and model
```

## 14. Cloudflare API Note

These backends are API endpoints. Cloudflare browser challenges must not be
required for `/health` or `/api/watch/explain-measurement-log`, because desktop
apps and ordinary HTTP clients cannot solve JavaScript challenges.

If the app receives HTML with `HTTP 403` and a header like:

```text
Cf-Mitigated: challenge
```

then Cloudflare, not the backend, challenged the request. The fix is to remove
Bot Fight Mode or Managed Challenge behavior for these API paths and use
Cloudflare block/rate-limit rules plus backend Basic Auth instead.
