# Gemini AI Access Security Decision

Status: decided; implementation pending.
Date: 2026-06-28

Implementation guide: `docs/for-ai/GEMINI_BACKEND_SETUP_GUIDE.md`

## Goal

Provide Gemini-powered app features without distributing the project-owned Gemini API key in the TimeGrapher client.

## Decisions

1. The project-owned Gemini API key must not be shipped with the app.
   - No hardcoding in source code.
   - No bundled config file.
   - No README or public repository exposure.

2. The only supported AI access mode uses a private backend server.

   ```text
   TimeGrapher App -> Private Backend Server -> Gemini API
   ```

   - The Gemini API key is stored only on the backend, preferably as an environment variable or secret.
   - The app sends requests to the backend and receives only the generated result.
   - The app does not know the Gemini API key or direct Gemini service details.

3. Grader credentials are provided separately, not embedded in the app.
   - The app contains only the login/input UI.
   - The demo ID/password are delivered through a private grading channel, such as LMS, email, or live presentation.
   - The backend verifies the credentials before calling Gemini.
   - If "remember login" or auto-login is implemented, store the credentials only in the operating system credential store.
   - On Linux/Raspberry Pi, this means a Secret Service-compatible keyring such as GNOME Keyring or KWallet; if no such keyring is available, do not persist credentials.
   - Treat a successful store/read/delete probe against the credential store as the availability check; a desktop session alone is not enough.
   - Do not store the password in app config, plain text files, screenshots, logs, crash reports, or bundled assets.
   - Keeping credentials only in memory is still acceptable when the user does not opt in to saving them.

4. The backend must limit misuse.
   - Use HTTPS.
   - Apply rate limits and daily quotas.
   - Limit request body size.
   - Fix allowed model and maximum output tokens on the server.
   - Expose feature-specific endpoints, not a generic Gemini proxy.

   Recommended endpoint style:

   ```text
   POST /api/watch/explain-measurement-log
   ```

   Avoid:

   ```text
   POST /api/gemini-proxy
   ```

5. Direct client Gemini access is out of scope.

   ```text
   TimeGrapher App -> Private Backend Server -> Gemini API
   ```

   - The app must not accept, store, or use a Gemini API key.
   - The app must not call Gemini directly.
   - All Gemini-powered explanations must go through the private backend.
   - The backend remains responsible for prompt ownership, model selection, token limits, quota, logging policy, and abuse controls.

6. Server-mode log upload is allowed only with explicit user consent.

   - The app may send a small analysis log file to the backend when the user requests AI explanation.
   - The UI must make clear that the log will be sent to the private backend for AI analysis.
   - The backend still owns the prompt template and combines the uploaded log with the server-side prompt before calling Gemini.
   - The backend must keep request size limits even if current logs are expected to be small.
   - The log-upload endpoint remains feature-specific and must not accept arbitrary prompts.

   Example flow:

   ```text
   User consent + AI explanation button
   -> App uploads measurement log
   -> Backend builds fixed prompt with the log
   -> Backend calls Gemini
   -> App displays the explanation
   ```

## Security rationale

This design treats the distributed client as outside the trusted boundary. Client-side secrets can be extracted, so the project-owned Gemini API key is isolated on the backend. Authentication, rate limiting, quota control, input limits, and narrow feature-specific endpoints reduce unauthorized use and cost-abuse risk.

Accurate claim for documentation and presentation:

> The Gemini API key is not distributed with the client. It is isolated as a server-side secret, and the backend reduces unauthorized use through authentication and request limits.

Avoid claiming that the system has "no security risk". The correct claim is that API key exposure risk is removed from the client and misuse risk is reduced by backend controls.

## Architecture boundary

- `TimeGrapher.Core` must not depend on Gemini, HTTP clients, UI, or platform-specific credential APIs.
- App-level services may coordinate the server-backed AI explanation flow.
- Backend-server integration should remain behind an app-facing service boundary so UI code does not know backend or Gemini protocol details.

## Korean summary

- 개발자 소유 Gemini API 키는 앱에 포함하지 않는다.
- AI 기능은 개인 서버 경유 방식만 지원한다.
- 앱은 Gemini API 키 입력, 저장, 직접 호출 기능을 제공하지 않는다.
- 채점자용 ID/PW는 앱에 넣지 않고 별도로 제공한다.
- 자동 로그인이나 로그인 정보 저장을 지원하는 경우, ID/PW는 운영체제 credential store에만 저장하고 평문 설정 파일에는 저장하지 않는다.
- 라즈베리파이/리눅스에서는 GNOME Keyring, KWallet 등 Secret Service 호환 keyring이 있는 경우에만 저장하고, 없으면 자동 로그인 저장을 제공하지 않는다.
- Desktop 환경인지보다 credential store에 실제 저장/조회/삭제가 되는지가 기준이다.
- 서버는 인증, rate limit, quota, 입력 크기 제한, 토큰 제한을 적용한다.
- 범용 Gemini 프록시가 아니라 기능별 API만 제공한다.
- 사용자의 명시적 동의를 받은 경우, 앱은 AI 설명을 위해 작은 분석 로그 파일을 서버로 보낼 수 있다.
