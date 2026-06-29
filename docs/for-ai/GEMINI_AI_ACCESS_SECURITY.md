# Gemini AI Access Security Decision

Status: decided; app-side implementation present.
Date: 2026-06-28

Implementation guide: `docs/for-ai/GEMINI_BACKEND_SETUP_GUIDE.md`

## Goal

Provide Gemini-powered app features without distributing the project-owned Gemini API key in the TimeGrapher client.

## Decisions

1. The project-owned Gemini API key must not be shipped with the app.
   - No hardcoding in source code.
   - No bundled config file.
   - No README or public repository exposure.

2. The only supported AI access mode uses approved private backend servers.

   ```text
   TimeGrapher App -> Approved Private Backend Server -> Gemini API
   ```

   - The Gemini API key is stored only on the backend, preferably as an environment variable or secret.
   - The app sends requests to the backend and receives only the generated result.
   - The app does not know the Gemini API key or direct Gemini service details.
   - Current approved backend base URLs are `https://tg-ai.jaehongoh.com` and `https://tg-ai-cmu-aws.jaehongoh.com`.

3. Grader credentials are provided separately, not embedded in the app.
   - The app contains only the login/input UI.
   - The demo ID/password are delivered through a private grading channel, such as LMS, email, or live presentation.
   - The backend verifies the credentials before calling Gemini.
   - If "remember login" or auto-login is implemented, store the credentials only in the operating system credential store.
   - The intended persistent-login targets are Windows through the OS credential store and the tested Raspberry Pi Desktop target through Secret Service/GNOME Keyring.
   - On Linux/Raspberry Pi, this means a Secret Service-compatible keyring such as GNOME Keyring or KWallet; if no such keyring is available, do not persist credentials.
   - Treat a successful store/read/delete probe against the credential store as the availability check; a desktop session alone is not enough.
   - SSH sessions may report `XDG_SESSION_TYPE=tty`; this does not prove the desktop keyring is unavailable. Run the credential-store probe from the graphical desktop session, or fail closed to in-memory credentials only.
   - The tested Raspberry Pi Desktop target reports `labwc:wlroots`, `rpd-labwc`, and `wayland`; `secret-tool store` opening a keyring password dialog is expected during GNOME Keyring creation or unlock.
   - On that target, enable persistent login only after the full `secret-tool store`, `lookup`, and `clear` probe succeeds.
   - This confirms the target environment can support credential-backed auto-login; the app implementation must still use the credential-store adapter and fail closed if the probe fails.
   - The Linux release installer should install `gnome-keyring` and `libsecret-tools` so Raspberry Pi users get the required Secret Service/keyring components during normal setup.
   - Update saved demo credentials only after a backend request succeeds, so failed authentication cannot overwrite or delete a previous saved login.
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

5. Direct client Gemini access is out of scope; all Gemini-powered explanations must use an approved private backend.

   ```text
   TimeGrapher App -> Approved Private Backend Server -> Gemini API
   ```

   - The app must not accept, store, or use a Gemini API key.
   - The app must not call Gemini directly.
   - All Gemini-powered explanations must go through an approved private backend.
   - The backend remains responsible for prompt ownership, model selection, token limits, quota, logging policy, and abuse controls.

6. Server-mode log upload is allowed only with explicit user consent.

   - The app may send a small analysis log file to the backend when the user requests AI analysis.
   - The UI must make clear that the log will be sent to the private backend for AI analysis.
   - The backend still owns the prompt template and combines the uploaded log with the server-side prompt before calling Gemini.
   - The backend must keep request size limits even if current logs are expected to be small.
   - The app keeps a client-side margin below the deployed `MAX_LOG_CHARS=100000` by rejecting logs above 90,000 characters before upload.
   - The log-upload endpoint remains feature-specific and must not accept arbitrary prompts.

   Example flow:

   ```text
   User consent + AI analysis button
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
- App-level services may coordinate the server-backed AI analysis flow.
- Backend-server integration should remain behind an app-facing service boundary so UI code does not know backend or Gemini protocol details.

## Korean summary

- 개발자 소유 Gemini API 키는 앱에 포함하지 않는다.
- AI 기능은 승인된 개인 백엔드 서버 경유 방식만 지원한다.
- 현재 승인된 백엔드 base URL은 `https://tg-ai.jaehongoh.com`과 `https://tg-ai-cmu-aws.jaehongoh.com`이다.
- 앱은 Gemini API 키 입력, 저장, 직접 호출 기능을 제공하지 않는다.
- 채점자용 ID/PW는 앱에 넣지 않고 별도로 제공한다.
- 자동 로그인이나 로그인 정보 저장을 지원하는 경우, ID/PW는 운영체제 credential store에만 저장하고 평문 설정 파일에는 저장하지 않는다.
- 자동 로그인 저장 대상은 Windows의 OS credential store와 확인된 라즈베리파이 Desktop의 Secret Service/GNOME Keyring을 기준으로 한다.
- 라즈베리파이/리눅스에서는 GNOME Keyring, KWallet 등 Secret Service 호환 keyring이 있는 경우에만 저장하고, 없으면 자동 로그인 저장을 제공하지 않는다.
- Desktop 환경인지보다 credential store에 실제 저장/조회/삭제가 되는지가 기준이다.
- SSH 세션의 `XDG_SESSION_TYPE=tty` 출력은 데스크톱 keyring 부재를 증명하지 않으므로, 그래픽 데스크톱 터미널에서 probe를 실행하거나 실패 시 메모리 저장만 사용한다.
- 확인한 라즈베리파이 Desktop 대상은 `labwc:wlroots`, `rpd-labwc`, `wayland` 세션이며, `secret-tool store` 실행 시 keyring 비밀번호 창이 뜨는 것은 GNOME Keyring 생성/해제 과정으로 정상이다.
- 해당 대상에서도 `secret-tool store`, `lookup`, `clear` 전체 probe가 성공한 경우에만 자동 로그인 저장을 활성화한다.
- 이 확인은 대상 환경이 credential 기반 자동 로그인을 지원할 수 있음을 의미하며, 앱 구현은 여전히 credential-store adapter를 사용하고 probe 실패 시 저장을 비활성화해야 한다.
- Linux 릴리즈의 `install.sh`는 `gnome-keyring`과 `libsecret-tools`를 설치하여 라즈베리파이 사용자가 일반 설치 과정에서 Secret Service/keyring 구성요소를 갖추도록 한다.
- 서버는 인증, rate limit, quota, 입력 크기 제한, 토큰 제한을 적용한다.
- 범용 Gemini 프록시가 아니라 기능별 API만 제공한다.
- 사용자의 명시적 동의를 받은 경우, 앱은 AI 분석을 위해 작은 분석 로그 파일을 서버로 보낼 수 있다.
