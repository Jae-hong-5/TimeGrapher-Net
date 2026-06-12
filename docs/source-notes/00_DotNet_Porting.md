# 세션 작업 기록 — TimeGrapher Qt/C++ → Avalonia/C# 포팅

> 작성 기준: 2026-06-05. 이 문서는 **세션 대화 기록만으로** 정리한 수정사항 로그다
> (코드 재참조 없이 대화에 기록된 내용 그대로).

## 현재 상태 업데이트 (2026-06-06)

이 문서는 초기 포팅 세션 기록이다. 현재 기준으로는 다음 내용이 추가로 반영되어 있다.

- 스플래시 화면은 생략 상태가 아니다. `splash5.mp4`를 640x360 PNG 122장으로 변환해 앱 시작 시 24fps로 재생한다.
- Raspberry Pi 5용 `linux-arm64 --self-contained true` publish와 `./TimeGrapher.App --smoke`가 통과했다.
- Pi GUI는 `DISPLAY=:0`에서 12초 동안 stderr 없이 실행 유지되는 것을 확인했다.
- 현재 남은 물리 검증은 실제 USB mic/capture source 연결 후 live microphone 입력 확인이다.

---

## 0. 선행 작업 (이전 세션, 2026-06-04)

| 항목 | 내용 |
|---|---|
| .NET SDK | 머신에 런타임만 있고 SDK 없음(exit 155) 확인 → winget으로 **.NET 8 SDK 8.0.421(LTS)** 설치 |
| Avalonia | **Avalonia.Templates 12.0.4** 설치, `avalonia.app` 템플릿 생성→복원→Release 빌드로 검증 |
| 함정 발견 | Avalonia 12 템플릿 기본 타겟이 `net10.0` → SDK 8 환경에서는 `-f net8.0` 필수 (NETSDK1045) |
| Pi 5 준비 | `check-pi-dotnet.sh` 점검/설치 스크립트 작성 (ARM64 런타임 + GUI 의존성 포함, WPF 제외 정책) |
| 스킬화 | 위 내용을 전역 스킬 `~\.codex\skills\avalonia-pi-env\`로 정리 (SKILL.md + 스크립트 동봉) |

---

## 1. 작업 목표

> **Qt/C++ 앱(TimeGrapher)을 Avalonia(C#)로 포팅 — UI/로직을 .NET으로 재작성해서 Windows에서 실행.**
> 원본: `D:\TimeGrapher_Refactoring\` (LG-CMU 소프트웨어 아키텍처 과정 팀프로젝트)

원본 앱: 시계 틱 오디오(라이브 마이크/WAV 재생/신호 시뮬레이션)에서 BPH·레이트 오차(s/d)·
비트 에러(ms)·진폭(°)을 실시간 추정, 스코프/레이트 플롯과 폴디드 사운드 이미지로 시각화.
규모 약 12,000줄 (C++ 소스/헤더 + MainWindow.ui 916줄).

---

## 2. 진행 순서와 수정사항

### 2-1. 원본 분석 (읽기만, 수정 없음)

- 원본 Qt/C++ 프로젝트의 `README.md` / `docs/ARCHITECTURE.md` / `AGENTS.md` 정독 — 컴포넌트 그래프, 스레드 경계,
  AnalysisFrame 계약, QAS-4(단일 프레임 일관성)/QAS-5(수정용이성) 확인.
- 핵심 헤더 정독: `SharedAudio.h`(30초 mono float 링버퍼), `Timegrapher.h`(tg_* C API),
  `AnalysisWorker.h`(프레임 DTO), `WatchMetrics.h`, `MainWindow.h`, 워커 헤더 3종,
  `GraphFrameRenderer.h`, `SoundImageRenderer.h`(공개부), `WavStreamWriter.h`.
- 샘플 WAV 9개 헤더 확인 → 전부 48 kHz / mono / 32-bit IEEE float (원본 OpenFile 검증과 일치).
- `WindowsAudio.cpp`(1472줄)는 OS 믹서/엔드포인트 설정 헬퍼(WindowsSetSoundParameters)임을 확인.

### 2-2. 신규 생성: C# 솔루션 스켈레톤 (`D:\tg_cld\TimeGrapherNet`)

직접 작성한 파일:

| 파일 | 내용 |
|---|---|
| `TimeGrapherNet.sln` | `dotnet new sln` + Core/App/Verify, 이후 WindowsAudio와 테스트 프로젝트 추가 |
| `src/TimeGrapher.Core/TimeGrapher.Core.csproj` | net8.0 classlib, UI/플랫폼 의존 없음 |
| `src/TimeGrapher.App/TimeGrapher.App.csproj` | net8.0 WinExe + Avalonia 11.3.2 + ScottPlot.Avalonia 5.0.55 |
| `src/TimeGrapher.Platform.WindowsAudio/TimeGrapher.Platform.WindowsAudio.csproj` | Windows live audio backend + NAudio 2.2.1 |
| `src/TimeGrapher.Verify/TimeGrapher.Verify.csproj` | net8.0 콘솔 (헤드리스 검증용) |
| `Shared/Argb.cs` | QRgb 대응 0xAARRGGBB uint 헬퍼 |
| `Shared/PixelBuffer.cs` | QImage(ARGB32) 대체 프레임버퍼 |
| `Shared/MasterAudioBuffer.cs` | TMasterAudioDataRaw 포트 (30초 링버퍼 + 카운터 + 통계, 동시성 계약 동일) |
| `Shared/AnalysisFrame.cs` | AnalysisFrame/GraphSeriesFrame/마커 3종/WatchMetricsUpdate/시리즈 id |
| `App.axaml(.cs)`, `Program.cs` | Avalonia 앱 셸 |
| `Views/MainWindow.axaml(.cs)` | 초기 단순 셸, 이후 전체 UI로 교체 |
| `01_PORTING_PLAN.md` | **포팅 고정 계약 기록** — 모듈별 공개 API 시그니처, 파일 소유권, 스레딩 규칙, 타입 매핑 규칙 |

핵심 설계 결정:
- **계약 우선(contract-first)**: 공유 DTO를 먼저 완성하고 모든 모듈의 공개 API를 `01_PORTING_PLAN.md`에
  고정 → 8개 에이전트가 병렬로 작업해도 모듈 간 시그니처 불일치가 없도록 함.
- 기술 매핑: Qt Widgets→Avalonia 11.3, QCustomPlot→ScottPlot.Avalonia 5.0,
  Qt Multimedia→NAudio(WaveInEvent), QImage→PixelBuffer→WriteableBitmap,
  QThread/signal→전용 Thread + AutoResetEvent + `Dispatcher.UIThread.Post`. WPF 미사용.
- MVVM 툴킷 없이 Qt 슬롯 구조에 1:1 대응되는 코드비하인드 방식 채택.
- 스켈레톤 첫 빌드로 사내망 NuGet 복원 조기 검증 (복원 2.6분, 오류 0).

### 2-3. 워크플로 병렬 포팅 (8개 에이전트, run `wf_b10cb858-1b5`)

각 에이전트는 `01_PORTING_PLAN.md` + 담당 C++ 원본을 정독 후 자기 소유 파일만 작성:

| 에이전트 | 원본 → 생성 파일 | 비고 |
|---|---|---|
| detector | Timegrapher.h + Detector.cpp(970줄) + Dsp + Bph → `Detection/` 5파일 | HPF→엔벨로프→온셋 검출→Rayleigh BPH→PLL 파이프라인 1:1 |
| metrics | WatchMetrics + RollingAverage/LeastSquares → `Metrics/` 3파일 | 결과 텍스트 포맷(자릿수/패딩) 재현 |
| soundimage | SoundImageRenderer(.h 566 + .cpp 1042줄) → `Imaging/` 1파일 | 절대 샘플 클럭/컬럼 경계/마커 deferred 배치/wrap 재구성 보존 |
| audioio | AudioWorker/PlaybackWorker/WavStreamWriter/WaveHeader/WindowsAudio → `AudioIo/` + `Platform.WindowsAudio/` | Windows NAudio 캡처, SystemAudioControl(엔드포인트 볼륨) |
| sim | WatchSynthStream(.h 326 + .cpp 567줄) + SimWorker → `Sim/` 2파일 | SplitMix64 PRNG까지 동일 알고리즘 포팅 |
| analysis | AnalysisWorker.cpp(362줄) → `Analysis/AnalysisWorker.cs` + Verify `Program.cs` | 4096 슬라이스 루프/마커/통계 동일 |
| renderer | GraphFrameRenderer.cpp + SoundImageWidget → `Rendering/` 관련 파일 | QCustomPlot→ScottPlot, 최신 구간 교체 방식으로 갱신 |
| mainwindow | MainWindow.ui(916줄) + MainWindow.cpp(895줄) + Main.cpp → `Views/MainWindow.axaml(.cs)` | .ui 절대 좌표 레이아웃 재현, 슬롯/세션/워커 수명주기 포트 |

- 실행 통계: 에이전트 9개(빌드 포함), 약 87만 토큰, 14분.
- 빌드 수정 루프: **1라운드 만에 오류 0** — 수정은 `GraphFrameRenderer.cs`의 CS0104
  모호 참조 2건(`Image`가 Avalonia.Controls vs ScottPlot 충돌 → 완전 한정)뿐.

### 2-4. 사후 수정 (직접)

| 수정 | 이유 |
|---|---|
| `Views/MainWindow.axaml.cs` — Sim 시작 시 Realistic 미체크 분기를 `WatchSynthStreamConfig.Default()` → **`Clean()`** 으로 변경 | 원본 MainWindow.cpp은 미체크 시 `watch_synth_stream_clean_config` 호출. 계약 문서에 Clean()이 빠져 있었는데 sim 에이전트는 원본 충실 원칙대로 Clean()을 이미 포팅해 둠 → 호출부만 원본과 일치하도록 정정 |
| `README.md` 신규 작성 | 빌드/실행 방법, 프로젝트 구성, 검증 상태, 의도적 차이 요약 |

### 2-5. 원본 대비 의도적 차이 (에이전트 보고 종합)

- `setRenderSource`(QObject 동적 프로퍼티 진단 기록) — Avalonia 대응 개념 없어 생략.
- QCP 마커 화살촉 글리프 → 단순 선분 근사 (위치/길이는 동일).
- 원본 Qt 동영상 스플래시는 MP4 직접 재생 대신 PNG 시퀀스 스플래시로 구현.
- Linux/Pi live audio는 PipeWire `pw-record`와 ALSA `arecord` fallback 방식으로 App 경계에 구현.
- AGC 비활성화(device-topology 순회) — NAudio 미노출로 엔드포인트 볼륨 설정만 수행.
- `LinuxSetSoundParameters`(Linux 마이크 캡처 볼륨 50% + AGC 비활성화, LinuxAudio.cpp) — 미포팅. `ConfigurePreferredInput`은 Linux에서 no-op이며, Pi의 마이크 볼륨/AGC는 시스템 기본값에 맡겨진다. `amixer` 기반 이식은 Pi 실기(마이크 연결) 검증과 함께 진행할 잔여 항목.
- `__uint128_t` 고정밀 경로 — MSVC에서 원래 비활성이므로 double 폴백 분기만 포팅.
- `GetRate(double&)` → `out double`, deque→LinkedList 등 C# 관용 대체 (결과 동일).
- 헤더 검증 실패 시 PlaybackDoneReadingFile 2회 emit → 1회로 정리.

---

## 3. 검증 결과 (세션 내 실행 기록)

| 검증 | 결과 |
|---|---|
| `dotnet build -c Release` | **오류 0**. 최신 Debug build 기준 경고 0 |
| 헤드리스 검증 (`TimeGrapher.Verify` ← samples 9개) | **9/9 BPH 정확 검출**, exit 0 |
| 검출 상세 | 18000/21600/28800 BPH 전부 일치, sync 전부 Synced, 레이트 +0.7~+20 s/d, 진폭 206–320°, 비트에러 0.0–1.3 ms |
| GUI 스모크 | 실행 후 10초 생존(PID 확인), 입력 장치 열거(실제 마이크 + Playback/Sim) · 48 kHz 기본값 · Averaging 12 로드 정상, 크래시 없음, 정상 종료 |
| 스플래시 검증 | Windows에서 스플래시 후 `TimeGrapher` 메인 창 전환 확인 |
| Raspberry Pi 검증 | `linux-arm64` publish, Pi `--smoke`, `DISPLAY=:0` GUI 12초 생존 확인 |

헤드리스 검증 출력(원문):

```
18000BPH_Watham.wav:            detected_bph=18000  RATE +20.0 s/d  AMP 290°  BE 0.6 ms
21600BPH_8215_InCase .wav:      detected_bph=21600  RATE  +4.9 s/d  AMP 277°  BE 0.9 ms
21600BPH_8215_jubilee .wav:     detected_bph=21600  RATE -17.4 s/d  AMP 282°  BE 0.0 ms
21600BPH_NH35.wav:              detected_bph=21600  RATE  +3.5 s/d  AMP 255°  BE 1.3 ms
21600BPH_NH39A.wav:             detected_bph=21600  RATE +17.7 s/d  AMP 320°  BE 0.4 ms
21600BPH_ST3600.wav:            detected_bph=21600  RATE +12.0 s/d  AMP 293°  BE 0.3 ms
28800BPH_3135_hulk.wav:         detected_bph=28800  RATE +16.7 s/d  AMP 269°  BE 0.0 ms
28800BPH_3235_FreeSprung.wav:   detected_bph=28800  RATE  +0.7 s/d  AMP 237°  BE 0.2 ms
28800BPH_3235_Starbucks.wav:    detected_bph=28800  RATE  +2.4 s/d  AMP 206°  BE 0.2 ms
```

---

## 4. 세션 중 발생한 문제와 해결

| 문제 | 해결 |
|---|---|
| Avalonia 12(템플릿 기본)와 ScottPlot.Avalonia 호환 불확실 | App을 Avalonia **11.3.2**로 명시 고정 (ScottPlot 5.0.55와 검증된 조합) |
| 병렬 에이전트의 동시 `dotnet build` 시 obj/ 잠금 충돌 우려 | 포팅 단계에서는 빌드 금지, 별도 빌드-수정 단계로 분리 |
| `Image` 타입 모호 참조 (CS0104) 2건 | Avalonia.Controls.Image로 완전 한정 (빌드 에이전트) |
| Realistic 미체크 시 sim config 불일치 | 원본 대조 후 `Clean()`으로 정정 (2-4 참조) |

---

## 5. 산출물 위치

- 포팅 결과: `D:\tg_cld\TimeGrapherNet\` (sln + Core/App/Platform/Verify, tests, README.md, docs/)
- 포팅 계약 기록: `docs/source-notes/01_PORTING_PLAN.md`
- 발표/리뷰 문서: `docs/`
- 전역 스킬: `C:\Users\admin\.codex\skills\avalonia-pi-env\` (SKILL.md + check-pi-dotnet.sh)
- 원본(무수정): `D:\TimeGrapher_Refactoring\`

## 6. 남은/후속 후보 작업

- 라이브 모드 실기 테스트 (실제 마이크 + 시계).
- Raspberry Pi live microphone 테스트: 현재 테스트 Pi에는 capture source가 없어 USB mic 연결 후 재검증 필요.
