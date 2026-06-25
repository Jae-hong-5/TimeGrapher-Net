# SAP 기준 Architecture Tactics & Design Patterns 분석

> CMU-LG Software Architecture Training Course 과제 문서.
> 기준 교재: Bass·Clements·Kazman, *Software Architecture in Practice* (이하 **SAP**).
>
> 이 문서는 TimeGrapherNet에 실제로 적용된 tactic·pattern을 **코드 근거로 검증**해 정리한다.
> 표의 마지막 열은 교과서 정의에 대한 적용도다 — **✓ 완전 적용**, **△ 유사하나 부분 적용**, **✗ 기각**.

## 개요 — 아키텍처를 지배하는 한 가지 문제

TimeGrapher는 시계 소리를 받아 실시간으로 분석·표시하는 앱이다(입력 → 검출 → 측정 → 화면).
실시간 앱이므로 설계는 세 가지 압력에서 출발한다.

1. **성능** — UI 주 스레드가 막히면 화면이 멈춘다.
2. **변경용이성** — 분석 로직이 UI·OS와 섞이면 바꾸기 어렵다.
3. **이식성** — Windows와 라즈베리파이 5를 한 코드로 돌려야 한다.

아래의 거의 모든 tactic은 이 세 압력에서 파생된다.

---

## 실시간 마감 예산 — 28800 BPH = 비트당 125 ms

성능 tactic들이 "무엇에 대한" tactic인지 정량적으로 못박는다. 기계식 시계는
BPH(시간당 진동수)마다 비트를 내며, 비트 주기 = 3600 s / BPH. 기준인 28800 BPH는
초당 8비트, 즉 **125 ms마다 한 비트**다. 오디오는 끊임없이 들어오므로 비트당
처리(캡처 → DSP/검출 → 메트릭 → 투영)가 비트 주기보다 오래 걸리면 백로그가 쌓인다.

| BPH | 18000 | 21600 | 28800 (기준) | 36000 | 43200 |
|---|---|---|---|---|---|
| 비트 주기 | 200.0 ms | 166.7 ms | **125.0 ms** | 100.0 ms | 83.3 ms |

### 처리 단위와 여유분

- 분석 패스는 **분석 블록 청크**(기본 4096샘플, 48 kHz에서 85.3 ms 분량; 설정 창에서 256–16384 조절 가능) 단위로 돌고
  (`AnalysisWorker.cs`), 패스 시작 시점의 스냅샷까지만 드레인하므로 한 패스가
  라이브 쓰기를 무한정 쫓지 않는다.
- 일시적 초과는 큐 증가가 아니라 **다음 패스의 더 큰 배치**로 흡수된다. 30초
  링버퍼는 28800 BPH 기준 **240비트 분량의 슬랙**이다. 그 이상 밀리면 가장 오래된
  샘플부터 드롭하고 `InputOverrun`/`InputSamplesDropped`로 계측한다
  (`MasterAudioBuffer.cs`).
- 증상 신호는 `AnalysisLagSamples`(패스 종료 시점 백로그)와
  `ProcessingElapsedMs`(패스 소요 시간)다.

### 단계별 비용 특성 (48 kHz, Pi 5 기준 정성 추정)

| 단계 | 비트당 비용 | 비고 |
|---|---|---|
| 캡처 콜백 + 링 쓰기 | ~µs | stackalloc 변환 + 2세그먼트 블록카피, 정상 상태 무할당 |
| DSP 체인(HPF→Envelope→Detector) | 코어의 ~0.05–0.2% | O(n) 스트리밍, 상수 상태 |
| 검출/메트릭 | ~µs | O(1) 증분 계산(Avg. Period 구간 누적기·PLL)은 재사용 스크래치로 무할당. 단 블록당 결과/메트릭 패키징(`DetectorMetricsBlockUpdate`·이벤트/PCM 스냅샷·이벤트별 `WatchMetricsUpdate`)은 블록당 할당 |
| 사운드프린트 컬럼 렌더 + 마커 | 수십 µs | 마커→컬럼 조회 O(1) (선형 탐색에서 개선) |
| 사운드프린트 발행 | ~0.5–1 ms, ≤10회/s | 고정 3버퍼 풀 복사(기본 크기 ~2.67 MB), LOH churn 0 |
| 스펙트로그램 STFT 컬럼 렌더 | 수십 µs | 1024-pt FFT/hop(48 kHz 기준 비트당 ~12 hop), 스크래치 재사용 무할당 |
| 스펙트로그램 발행 | ~0.25 ms, ≤10회/s | 사운드프린트와 동일한 고정 3버퍼 풀 복사(~1.92 MB) |
| UI (활성 탭 1개) | 33/100 ms 스로틀 | 32000/250 포인트 예산, latest-wins 합류, 마커 plottable 풀링. pause 종료 또는 Long-Term 탭 이탈의 리뷰 커서 제거만 `RenderToAll` 1회 예외 |

### 마감 강제 — AnalysisDeadlineMonitor

측정만 하던 텔레메트리에 반응을 붙였다(tactic: **bound execution times / manage
work requests** — 점진적 저하). 패스마다 백로그를 **비트 주기 단위**(공칭 락
주기 `MeasuredPeriodS`(3600/BPH), 락 전에는 125 ms 기본)로 환산해 2비트 예산과
비교하고, 연속 16패스 초과 시 시각 비용이 싼 순서로 저하한다:

1. 진행 중 사운드프린트 컬럼과 스펙트로그램 라이브 에지 커서의 실시간 갱신 중단
2. 사운드프린트·스펙트로그램 발행 간격 100 ms → 400 ms, 스윕·멀티필터 시리즈
   발행 플로어 50 ms → 400 ms (지속 2비트 위반 중에는 패스당 스트림 전진이
   250 ms 이상이므로, 플로어가 그보다 길어야 위반 도중에도 게이트가 닫힌다)
3. 스코프 데시메이션 stride 2배 + 신규 비트 세그먼트 윈도 개방 중단
   (Beat-Noise 탭이 전진을 멈춤; 열린 윈도는 자연 완료)

연속 48패스 회복(0.5비트 미만) 시 한 단계씩 복귀한다(히스테리시스로 진동 방지).
현재 레벨은 프레임에 실려 상태바에 "rendering quality reduced"로 표시된다
(`AnalysisDeadlineMonitor.cs`, `AnalysisRunStatusReporter.cs`). 백로그(lag)를 위반
신호로 쓰는 이유: 단일 패스 소요는 분석 블록(기본 4096샘플)으로 바운드되어 정규화 없이는 예산과
비교할 수 없고, 백로그가 곧 "비트당 작업 > 비트 주기"의 적분적 증상이기 때문이다.

남은 검증: Pi 5 라이브 마이크 실측으로 단계별 비용 추정을 수치로 대체하는 것.

---

## 1. Architecture Tactics (품질속성별)

### 변경용이성 (Modifiability)

| Tactic | 적용 방식 | 근거 | |
|---|---|---|---|
| **restrict dependencies** | Core는 외부 참조 0개. App → Core / Platform.*, Platform.* → Core 단방향 비순환(App은 Core를 직접 참조하고 플랫폼 어댑터는 RID 조건부로 참조). **CI가 Core의 `.csproj` 참조와 금지 using(UI·플랫폼·NAudio)을 차단**하고, OS별 publish가 해당 플랫폼 어댑터만 포함하는지 검증 | `TimeGrapher.Core.csproj`, `.github/workflows/ci.yml` | ✓ |
| **encapsulate** | OS 오디오 스택(NAudio / pw-record)을 Core 소유 인터페이스 `ILiveAudioWorker : IAudioInputWorker` 뒤에 은닉 | `ILiveAudioWorker.cs`, `IAudioInputWorker.cs` | ✓ |
| **use an intermediary** | `LiveAudioBackend` 한 파일만 구체 OS 타입을 알고 분기. 나머지 App은 인터페이스만 사용 | `LiveAudioBackend.cs` | ✓ |
| **increase semantic coherence** | Core = 분석 도메인(Detection/Metrics/Imaging/Sim/Analysis/AudioIo/Shared)만 담당. UI·OS 책임 없음 | `TimeGrapher.Core.csproj` | ✓ |
| **split module** | 비대해지던 `MainWindow`를 partial 5개 + 추출 서비스(`RunCommandService`, `RunSessionController`, `MainWindowSelectionCoordinator`)로 분해. 추출 서비스는 `IRunCommandOperations`·`IMainWindowSelectionOperations`·`ITimeGrapherDialogService` 시임 뒤에서 View와 분리되어 fake 구현으로 GUI 없이 단위 테스트된다 | `MainWindow.*.cs`, `RunCommandService.cs` | ✓ |
| **condition monitoring + fault detection (기본 검출 동작)** | 적응 플로어와 레짐 가드를 옵션 seam이 아니라 기본 검출 알고리즘으로 흡수. 기준선 보존보다 정확성과 실시간 비용을 우선해 샘플 루프의 all-off 분기를 제거하고, 별도 baseline-equivalence 게이트를 폐기(두 메커니즘 모두 토글 없는 기본 동작 — 옵트인으로 남은 검출 노브는 약한-A onset 구제(`WeakAOnsetRescue`→`PhaseGuideOnsetRescueScale`) 하나뿐, 124행) | `Detector.cs`, `AdverseScenarios.cs`, `AdaptiveFloorTests.cs`, `RegimeGuardTests.cs` | ✓ |
| **fault detection — 약한 A onset 구제 (opt-in)** | worst-case B→A는 약한 A가 onset 트리거 아래로 묻혀 detector가 B에 lock하는 것이다. post-lock phase-guide 윈도우의 onset/peak threshold를 결정론적으로 낮추는 `PhaseGuideOnsetRescueScale`(0 = off, 기본)을 추가해 묻힌 A를 후보로 살린다. App "Weak-A onset rescue" 토글이 켜지면 `AnalysisRunSettings.WeakAOnsetRescue`가 1.0으로 매핑되고 꺼지면 0.0이라 검출 경로는 비트-동일(골든 마스터 보존). 정상 arm을 건드리지 않는 옵트인 seam | `Detector.cs`, `TgTypes.cs`, `AnalysisRunSettings.cs`, `PhaseGuideRescueTests.cs` | ✓ |
| **abstract common services + use an intermediary (정상 밴드 단일 소스, QAS-4)** | Error Rate/Amplitude/Beat Error의 "정상" 밴드 min/max를 가변 단일 소스 `AcceptBandSettings.Current`로 통합한다. 측정별 정책(`TraceAlertEvaluator`·`VarioGaugePolicy`·`BeatErrorDiagnostics`, `LongTermAcceptPolicy`가 집約)이 상수 대신 이 값을 읽어 **모든 그래프·판정·배지·기준 플라이아웃이 같은 숫자로 정상 판정**(일관성 driver). 사용자가 Settings에서 편집하면 `IAcceptBandConsumer`(테마용 `IThemedFrameConsumer`를 미러한 두 번째 브로드캐스트 계약) 팬아웃 — `GraphFrameRenderer.ApplyAcceptBands()`가 Vario/Trace/Long-Term/Beat-Error 렌더러에 **누적 히스토리를 보존한 채(run reset 아님)** 라이브 반영하고 정지 중에도 즉시 갱신. `AcceptBandSettingsStore`가 사용자 설정 폴더(`%APPDATA%`/`~/.config`) JSON으로 영속화하고 시작 시 `Program.Main`에서 복원(누락/손상/비유한/범위초과는 `IsValid`로 거르고 기본값 폴백 — 시작 차단 금지). 표시 정책이라 **Core 무의존(App 계층)**, ViewModel은 5개 십진 값만 노출하고 Rendering에 결합하지 않는다 | `AcceptBandSettings.cs`, `AcceptBandSettingsStore.cs`, `IAcceptBandConsumer.cs`, `GraphFrameRenderer.cs`, `LongTermAcceptPolicy.cs` | ✓ |
| **abstract common services (테마 색 단일 소스 + 글래스 레이어)** | UI 크롬과 스코프 그래프의 **모든 테마 색을 `App.axaml`의 `ThemeDictionaries`(Light/Dark) 한 블록**에 정의하고, 브러시는 그 `Color` 키에서 파생하며 그래프 렌더러도 `PlotThemePalette.FromResources`로 같은 키를 읽는다 — 앱을 리컬러할 때 이 한 곳만 고치면 UI·그래프가 함께 따라온다(일관성 driver). 글래스모피즘("사파이어 크리스털") 표면도 같은 단일 소스(`App.axaml`)에 토큰(`AmbientBackdropBrush`/`GlassPanelBrush`/`GlassRimBrush`/`GlassShadow`)을 추가하고 재사용 `Border.GlassCard` 스타일로 칠한다 — 단, 이 글래스 톤은 팔레트 `Color` 키에서 파생하지 않고 Light/Dark 블록마다 정의된 리터럴 값이다(투명 프로스트/베벨 톤이라 팔레트 색과 별개). 활성 `ThemeDictionary`가 브러시 자체를 교체하므로 테마 토글에는 자동 반응하고(테마별 단일 소스 리컬러), 전역 `Border { CornerRadius=0 }` 규칙으로 0px 칼각(정밀 계측기 정체성)을 유지한다 | `App.axaml`, `PlotThemePalette.cs`, `PlotThemeHelper.cs` | ✓ |

### 성능 (Performance) — 실시간 UI의 핵심

| Tactic | 적용 방식 | 근거 | |
|---|---|---|---|
| **introduce concurrency** | 분석과 합성/재생 입력 워커는 `ThreadPriority.Highest`, 녹음 writer는 `Normal`로 각자 전용 스레드에서 돌고, 라이브 캡처(Windows `WaveInEvent` 콜백·Linux stdout/stderr 리더 스레드)는 .NET 스레드 우선순위를 따로 지정하지 않는다. UI 스레드는 렌더링만. 생산자는 소비자를 기다리지 않음. ⚠ .NET `Thread.Priority`는 Linux에서 no-op이라 Pi에서는 우선순위가 무효과이며 실제 보호 장치는 바운드 큐 구조다(5.1절) | `AnalysisWorker.cs`, `SimWorker.cs`, `PlaybackWorker.cs`, `QueuedWavStreamWriter.cs`, `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs` | ✓ |
| **limit event response** | 렌더 스케줄러가 **"최신 프레임 1개"만 유지** — 렌더 진행 중 들어온 프레임은 병합/폐기(`_droppedFrames`)하고, 일회성 신호(오버런 등)는 병합으로 보존 | `AnalysisFrameRenderScheduler.cs` | ✓ |
| **schedule resources** | 일반 프레임은 모든 탭이 가벼운 `ObserveFrame`만 받고 **활성 탭만** 무거운 `RenderFrame` 수행. pause 종료 또는 Long-Term 탭 이탈 때 리뷰 커서를 지우기 위한 `RenderToAll`은 저장된 마지막 프레임을 한 번 다시 그리는 예외라 입력/분석 작업을 늘리지 않음 | `AnalysisFrameRouter.cs`, `MainWindow.axaml.cs` | ✓ |
| **bound queue sizes** | 녹음 큐 = `BlockingCollection(128)`. 초과 시 블록을 **드롭**(분석 스레드를 막지 않음) | `QueuedWavStreamWriter.cs` | ✓ |
| **reduce overhead** | 롤링 집계 O(1)(`RollingAverage/LeastSquares`), 그래프 점 수를 예산(scope 32000 / rate 250, `InfoTabCatalog`)으로 `SeriesDataReducer`가 다운샘플, `ArrayPool`·비트맵 재사용 | 다수 | ✓ |
| **manage sampling rate** | 입력 워커를 Stopwatch 기준 10ms 주기로 페이싱; 노이즈 플로어를 매 샘플이 아닌 ~1ms마다 데시메이션 | `SimWorker.cs`, `Detector.cs` | ✓ |
| **maintain multiple copies of data** | 30초 링버퍼로 읽기/쓰기 속도 분리 + 사운드프린트 발행은 **고정 3버퍼 풀을 로테이션**하는 스냅샷 복사(발행된 버퍼는 2회의 더 새로운 발행 이후에만 재사용 → UI가 안전하게 읽는 동안 분석 스레드는 계속 갱신, 정상 상태 할당 0). 스펙트로그램 STFT 이미지 발행도 **동일한 고정 3버퍼 풀 로테이션**을 재사용한다. 비트 노이즈 세그먼트 발행도 같은 패턴: **고정 28버퍼 풀(float[1600])에서 발행 기준으로 재사용을 게이트**하고, envelope / raw-min / raw-max 버퍼가 같은 pool index를 공유해 하나의 발행 보호 규칙으로 세 버퍼를 함께 보호한다. 최근 envelope ring은 구성된 event-gate post-window + 5 ms pre-roll + 1 analysis block + 1 sample guard에서 산출(최소 `BaseEnvelopeRingSeconds` 0.6초 바닥과의 max)해 delayed post-gate event도 같은 원본 envelope를 읽고, 병렬 raw ring은 실제 양극성 PCM window가 덮어쓰기 전에 min/max로 발행될 만큼 detector envelope delay lead를 추가로 보존한다. 완료 링과 최근 2개 스냅샷이 참조하는 버퍼는 재사용 스캔에서 제외되어, UI가 읽는 동안 불변 계약 유지. Beat-Noise/Waveform 렌더러는 버전이 바뀔 때만 스냅샷을 UI 소유 사본으로 깊은 복사(`CopyForCache`→`_lastSnapshot`)해, 이후 상호작용·일시정지 리뷰 재렌더가 재사용된 풀 버퍼를 읽지 않도록 한다 | `MasterAudioBuffer.cs`, `SoundPrintFrameProjector.cs`, `SpectrogramFrameProjector.cs`, `BeatSegmentCapture.cs` | ✓ |
| **bound execution times / manage work requests** | `AnalysisDeadlineMonitor`가 패스 백로그를 **비트 주기 단위**로 환산해 2비트 예산 초과가 지속되면 점진 저하 사다리(라이브 프리뷰 중단 → 발행 간격 확대 → stride 증가) 실행, 지속 회복 시 단계 복귀. 스펙트로그램 프로젝터도 같은 노브(라이브 에지 커서 중단, 발행 간격 4배)를 노출해 사다리 레벨 1·2에 함께 배선되어 있다. 위 "실시간 마감 예산" 절 참조 | `AnalysisDeadlineMonitor.cs`, `AnalysisWorker.cs` | ✓ |
| **bound resource usage (장기 히스토리)** | 비트 단위 메트릭 히스토리(`BeatMetricsHistory`)를 **고정 용량 `DecimatingSeries`**에 누적 — 가득 차면 인접 포인트 쌍을 병합해 해상도를 반감(버킷 min/max 보존). 실행이 몇 시간이어도 메모리·발행 비용이 일정("1시간째 비용 = 1초째 비용"). 스냅샷은 비트당 최대 1회 재구성(빠른 시계는 `PublishRateCapBph`=24000 주기 0.15초로 상한, 락 전에는 상한 주기 사용), 그 사이 프레임은 같은 불변 인스턴스 공유. 다중 포지션 시퀀스도 `WatchPositions.Count`(10) 슬롯 배열과 스냅샷 버전 게이트로 제한된다. 위치 전환 타임라인(`PositionChanges` — `PositionChange(TimeS, Position)` 리스트)만은 수동 전환 횟수에 비례해 스냅샷마다 복사되지만(`_positionChanges.ToArray()`, Long-Term 그래프가 파선(dashed) 위치-전환 마커로 소비), 전환이 수초~수분 간격이라 비트당 경로 대비 무시할 수준이다. 누적은 Core에서 수행 — 렌더 스케줄러의 latest-wins 병합이 프레임을 폐기해도 데이터 손실 없음 | `DecimatingSeries.cs`, `BeatMetricsHistory.cs`, `MultiPositionSeqRenderer.cs`, `LongTermPerfRenderer.cs`, `AnalysisWorker.cs` | ✓ |
| **record/monitor (레이턴시·측정 결과 증거)** | QA가 요구하는 캡처→처리→표시 레이턴시를 단일 Stopwatch 시계로 계측: `MasterAudioBuffer`가 쓰기마다 (sampleEnd, ticks) 256개 스탬프 링을 유지, `AnalysisWorker`가 프레임에 `CaptureTimestamp`/`ProcessingCompletedTimestamp`를 스탬핑, UI가 렌더 직후 표시 시각을 더해 구간별 평균/최악값을 집계(`LatencyStatsTracker`, 상태바 우측 표시). `--analysis-log <csv>`는 같은 GUI 렌더 이후 지점에서 캡처→처리, 처리→표시, 전체 지연, 누적 평균/최악값, 드롭 샘플, 누락 비트를 CSV로 남긴다. `--measurement-log <csv>` 또는 Settings 창의 measurement CSV 토글은 **실행(run) 시작 시점마다** 새 로그를 열어 상단에 그 실행이 사용하는 lift angle 설정값을 기록한 뒤, 렌더된 `BeatMetricsHistorySnapshot` 버전마다 세션/프레임 id, 경과 시간, BPH, rate, amplitude, beat error, 파생 timing 지표, 안정도 통계, 누락 비트와 싱크 손실을 CSV로 남긴다(일시정지/재개는 같은 파일에 계속 기록). 첫 실행은 `--measurement-log` 경로가 있으면 그 경로를, 이후 실행은 실행 파일 폴더의 `log/yyyyMMdd_HHmmss.csv`를 자동 생성해 쓴다. 고객에게 보이는 오류 문구는 원인·경로·샘플 수 같은 진단 세부 정보를 제거하고, 같은 발생 지점에서 `UserErrorLog`가 실행 파일 폴더의 `log/error.log`에 ISO-8601 시각과 상세 원인을 append한다. 누락 비트(`WatchMetrics.MissedBeats`)·싱크 손실은 **세션 누적 카운터**로 프레임에 실려 latest-wins 병합에도 보존 | `MasterAudioBuffer.cs`, `LatencyStatsTracker.cs`, `AnalysisPerformanceLogger.cs`, `MeasurementResultLogger.cs`, `UserErrorLog.cs`, `SettingsWindow.axaml` | ✓ |

### 가용성 (Availability) — 시작/중지 안정화와 결함 입력 처리

| Tactic | 적용 방식 | 근거 | |
|---|---|---|---|
| **timestamp (논리 시퀀스)** | **핵심.** 실행마다 단조증가 `_runSessionToken`을 발급, 모든 비동기 콜백이 토큰을 들고 옴 → 이전 실행의 늦은 응답을 토큰 불일치로 폐기(`AnalysisSessionId`, 렌더 `_generation`까지 3중) | `RunSessionController.cs` | ✓ |
| **exception handling / detection** | 워커 스레드는 예외를 try/catch로 가둬 프로세스를 죽이지 않고 실패로 보고한다(`PlaybackWorker`는 `DoneReadingFile`에 `PlaybackCompletionReason.Failed` 전달). 라이브 캡처 워커는 `_stopRequested`로 "정상 중지"와 "장치 사망"을 구분해 후자에서만 `CaptureEnded`를 발생시킨다 | `PlaybackWorker.cs`, `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs` | ✓ |
| **sanity checking (캡처 설정)** | Live 입력은 공유 표준 sample rate를 표시 전에 플랫폼별로 검증한다. Windows는 선택된 WinMM 장치 이름을 활성 WASAPI 캡처 엔드포인트와 매칭한 뒤 mono shared float 포맷으로 지원 rate를 먼저 프로브하고(mono 프로브가 빈 목록을 반환하면 mix-format 채널 수로 rate를 폴백 발견하며, 실제 캡처는 항상 mono IEEE float `WaveInEvent`), Linux는 동일한 rate·mono 설정으로 `pw-record`/`arecord` probe를 짧게 열어본다. 엔드포인트가 애매하거나 rate가 없으면 임의 선택하지 않아 잘못된 runtime 구성을 시작 전에 차단한다 | `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs`, `MainWindow.AudioSetup.cs` | ✓ |
| **fault recovery (명시적 복구 상태)** | `RunCommandService`가 State Pattern으로 `Stopped`/`Starting`/`Running`/`Paused`/`Stopping`/`StopFailed` 전이를 소유한다. 수동 Stop은 stop 성공 뒤 표시/프레임 상태만 reset하고 device refresh는 실행하지 않아 사용자가 고른 Live/Playback/Simulation 선택을 보존한다. paused reset은 `ResetAfterStop` 의도를 보존해 stop 성공 뒤에만 reset+device refresh를 실행하고, live capture unexpected end는 `RefreshDevicesAfterStop` 의도로 stop 성공 뒤에 device list만 갱신한다. worker timeout 또는 recording close 실패는 `StopFailed`로 보내 재시도를 계속 열어 둔다 | `RunCommandService.cs`, `RunCommandService.States.cs`, `RunCommandServiceTests.cs`, `MainWindow.RunLifecycle.cs` | ✓ |
| **degradation** | Linux 입력 장치 열거는 PipeWire `wpctl` 결과가 없으면 ALSA `arecord -l`로 폴백한다. 사용자가 선택한 ALSA 장치는 `arecord`/S16_LE 경로로 캡처되어 PipeWire 장치가 없어도 낮은 수준의 캡처 경로를 제공한다 | `LinuxLiveAudioWorker.cs` | ✓ |
| **ignore faulty input + state resynchronization (검출 갭)** | 반 비트를 초과하는 A-A 간격을 단일 기준으로 "검출 갭"으로 분류해 3중 대응: ① 갭에 걸친 부호 비트오차/주기 델타를 무효화(ignore faulty input) ② 틱/톡 비트 카운터를 물리 위상에 재앵커 ③ Avg. Period 상단바 판독 누적 구간을 갭에서 재시작(state resynchronization, 새 싱크 락과 동일한 회복 — 설정된 평균 구간이 다시 완료되면 판독 복귀). 비트 1개 누락이 부호를 반전시키거나 누적 통계 min/max를 영구 오염시키지 않으며, 누락 비트는 보간 없이 제외되고 `MissedBeats` 세션 카운터로 기록된다 | `WatchMetrics.cs` | ✓ |
| **startup transient 억제 (완료 구간 판독)** | 상단바 Error Rate/Amplitude/BEAT ERROR는 Settings의 Avg. Period 구간이 완료될 때까지 무효로 보류한다. 완료 시 Error Rate는 tic-to-tic/toc-to-toc 같은 위상 period 차이 평균을 s/24h로 변환하고, Amplitude는 tic/toc 쌍평균 진폭의 구간 평균, BEAT ERROR는 절대 beat error의 구간 평균을 표시한다. 다음 구간이 완료될 때까지 마지막 완료 구간 값을 유지한다. 그래프/히스토리 이벤트 시리즈는 기존 이벤트 샘플(RLS rate, 부호 beat error, tic/toc 쌍평균 진폭)을 계속 사용해 상단바 smoothing과 분리하되, 완료된 Error Rate 구간은 `AveragePeriodRateInterval`로 누적해 Rate/Scope와 Beat Error 그래프에 구간 annotation과 구간별 Error Rate/Amplitude/BEAT ERROR 라벨로 노출한다. Amplitude/BEAT ERROR 완료가 rate 구간 생성보다 늦으면 같은 interval을 교체 갱신해 구간은 중복되지 않는다. 새 싱크 락과 검출 갭 복구가 같은 구간 완료 조건을 공유하며, 측정 중간의 미완료 구간은 표시값을 흔들지 않는다. 측정: 깨끗한 nominal 비트에서 기본 2초 Avg. Period가 끝나는 시점에 첫 유효 판독이 발생 | `WatchMetrics.cs`, `BeatMetricsHistory.cs`, `AveragePeriodRateAnnotations.cs`, `WatchMetricsDerivedMeasuresTests.cs` | ✓ |
| **condition monitoring + degradation recovery (약신호)** | 적응 플로어는 옵션이 아니라 기본 검출 동작(`Init`이 `RejectedPeakMinSnr`·`AdaptiveFloorMinMul`·`RefDecayAfterS`·`RefDecayTauS`를 상시 설정): 기각 버스트가 섀도 중앙값 통계를 남겨(condition monitoring) 10×노이즈 하드 플로어(minPeakThr ≈ 2.8×n) 아래의 약한 시계로 기준이 하향 적응하고, 수락 공백 후 기준 피크가 지수 감쇠 + 히스토리 재시작으로 큰소리→조용함 래치를 자가 복구(degradation recovery). 측정: 게인 스텝(큰소리→조용함) 후 기본 검출기가 재락 — quiet-step 행 recall/precision 1.000/1.000(게이트 0.95), 적응 회복은 `LoudToQuietTransition_AdaptiveFloorDecaysAndReacquires`가 감쇠 지평 내 재획득으로 검증 | `Detector.cs`, `AdaptiveFloorTests.cs` | ✓ |
| **sanity checking + ignore faulty input (PLL-guided onset gating)** | BPH/PLL lock 이후에는 sync tracker가 예측한 A phase 안에서만 새 A burst를 열고, 그 guided window에서는 긴 beat period일수록 onset 기준을 더 강하게 적용한다. 동기화된 시간 기준으로 이른 노이즈 crossing을 결함 입력으로 배제해 한 beat를 선점하지 못하게 막으면서, phase 안의 진짜 약신호는 min-peak 기준 완화로 받아들인다. 측정: weak-2 recall/precision 0.043/0.043 → 0.957/0.957, noisy-1 0.167/0.167 → 0.722/0.722, noisy-2 0.146/0.146 → 1.000/1.000, quiet-step 1.000/1.000 유지 | `TgDetector.cs`, `Detector.cs`, `DetectorStressScenarioTests.cs`, `AdverseScenarios.cs` | ✓ |
| **fault detection with hysteresis (임펄스 잡음)** | 레짐 가드(기본 동작, 별도 플래그 없음): V5.6 순간 레짐 트립을 `TG_REGIME_TRIP_BEATS`(=3) 길이 지속 카운터 `RegimeTripRun`으로 디바운스 — 단발 임펄스(문 쾅)는 다음 정상 틱이 런을 리셋해 전체 플러시(BPH/PLL/히스토리 소거)를 일으킬 수 없고, 진짜 이득 변화는 ~3비트 내 정상 트립. 측정: impulse-dos 행 리셋 7→0, NotSynced→Synced | `Detector.cs`, `RegimeGuardTests.cs` | ✓ |

> **악조건 측정값 표기 주의(코드=진실).** 위 가용성 표의 화살표 **뒤(after) 수치는 현재 `Verify --adverse` 출력과 일치**해 재현 가능하다(검증: weak-2 0.957/0.957, noisy-1 0.722/0.722, noisy-2 1.000/1.000, quiet-step 1.000/1.000, impulse-dos resets 0). 화살표 **앞(before) 수치는 기능 도입 시점의 baseline 측정값**으로, all-off 분기가 제거된 현재 코드로는 재현되지 않고 코드/테스트에 저장돼 있지 않다(개선 폭 맥락 제공용 역사적 근거). 코드가 강제하는 것은 `AdverseScenarios`의 게이트(`MinRecall`/`MinPrecision`/`MaxResets` 등)다.

### 시험용이성 (Testability)

| Tactic | 적용 방식 | 근거 | |
|---|---|---|---|
| **sandbox + limit nondeterminism** | `WatchSynthStream`이 SplitMix64 PRNG를 **시드 고정**해 결정론적 시계 신호 생성. `Clean()`은 사실적 변동 요소(패킷·공진·드리프트·임펄스 잡음)를 끄지만 미세한 백색잡음 바닥(`NoisePeakSignalLevel`≈0.0005)은 남는다 — 무작위 제거가 아니라 **시드 고정**이 재현성을 보장한다(같은 시드 → 비트동일 출력) | `WatchSynthStream.cs` | ✓ |
| **abstract data sources** | mic·WAV·합성이 모두 `IAudioInputWorker`/`engine.Process(span)` 뒤에서 동일하게 소비되어, 파일로 결정론적 검증 가능 | `DetectorMetricsEngine.cs` | ✓ |
| **specialized interfaces** | GUI 없는 `Verify` 콘솔(종료코드 0/1/2), 앱의 `--smoke`(0)·`--audio-smoke`/`--capture-smoke`(0/2/3)·`--analysis-benchmark`(0/1) 진입점, `InternalsVisibleTo` 테스트 훅 | `Verify/Program.cs`, `Program.cs`, `AudioSmokeRunner.cs`, `AnalysisBenchmarkRunner.cs` | ✓ |
| **executable assertions** | Verify가 파일명의 기대 BPH와 검출 BPH를 대조해 exit code 반환 → **CI가 main 브랜치 push·main 대상 PR에서 실행**. `FillF32` 그라운드트루스 사이드채널 채점(`DetectionScorer` — 이벤트 수준 정밀도/재현율/타이밍)은 이제 생성 fixture의 hard gate이며, 생성 fixture 안에 +30 s/day rate와 5 ms beat-error 표본을 포함해 메트릭 값도 exit code에 묶는다. `--adverse` 악조건 행은 현재 기본 detector 품질 게이트로 실행 | `Verify/Program.cs`, `AdverseScenarios.cs`, `DetectionScorer.cs` | ✓ |
| **record/playback (기준선 회귀 감시)** | 골든마스터는 절대 이벤트 시퀀스 드리프트 감지용으로 유지하되, null vs all-off 옵션 패리티와 Verify `--fidelity-check`는 제거. 기준선 약점 보존보다 정확성과 성능을 우선한다 | `DetectorGoldenMasterTests.cs`, `AdverseScenarios.cs` | ✓ |
| **controlled fault injection** | 합성기에 포아송 임펄스 잡음 노브(전용 RNG 스트림 — 켜도 틱/지터 시퀀스 비트동일, rate 0이면 출력 비트동일). 균일 백색잡음으로는 재현 불가능하던 레짐 리셋 폭풍·중앙값 오염·PLL 래치를 결정론적으로 재현 | `WatchSynthStream.cs`, `WatchSynthImpulseNoiseTests.cs` | ✓ |
| **limit structural complexity** | 파서·리듀서·라우터·서비스를 작은 단일책임 단위로 분리, 현재 테스트 소스 119개(앱 73, Core 43, WindowsAudio 1, LinuxAudio 1, Verify 1)가 개별 타깃을 검증 | tests/ | ✓ |

### 사용성·이식성 (Usability / Portability)

| Tactic | 적용 방식 | 근거 | |
|---|---|---|---|
| **pause/resume** (Usability) | `WorkerPauseGate`(ManualResetEventSlim + Volatile)가 워커 루프를 50ms 슬라이스로 멈추되 정지 요청에는 즉시 반응 | `WorkerPauseGate.cs` | ✓ |
| **UI 일관성 — 그래프 레이아웃 안정성** (Usability) | 스코프/그래프의 좌측 Y축·하단 X축 패널을 고정 px로 잠가(`RateScopeRenderer`의 택틱을 `TraceDisplayRenderer`·`LongTermPerfRenderer`로 확장) Y 눈금 자릿수가 바뀌어도 데이터 영역 폭이 일정하고 스택 패널들의 좌측 가장자리가 정렬된다. 또한 Trace 디스플레이는 live-follow에서 plain `AutoScale`(데이터에 margin을 더하고 모든 보이는 플로터블에 맞춤) 대신 X축을 실제 데이터 시간 범위에 고정(`PinLiveXAxisToData`→`SetLimitsX`, `LongTermPerfRenderer` 택틱)한다 — accept-범위 라벨이 autoscale opt-out 없는 `Text` 플로터블이라, 매 프레임 margin 안쪽 우측 가장자리로 재배치되며 다음 AutoScale의 X 적합에 되먹임돼 축을 데이터 너머로 래칫시키고 트레이스를 가로로 축소시키던 현상을 차단한다. Rate/Scope와 Beat Error의 Error Rate 그래프는 같은 120-beat 페이지 X축을 사용해 0..120, 120..240처럼 경계에서만 넘어가므로 점이 추가될 때마다 데이터 영역이 가로로 흔들리거나 스크롤하지 않는다. 같은 두 그래프는 `AveragePeriodRateAnnotations`를 공유해 Avg. Period 완료 구간 경계와 그 s/d 값을 동일하게 표시한다. Rate/Scope의 Reset View는 두 그래프 위 overlay 버튼 2개 대신 Beat Error와 같은 `*,Auto` 헤더 스트립의 우측 고정 슬롯 버튼 1개로 통합해 플롯 내부를 가리지 않고 탭 간 제어 위치를 맞춘다. Trace와 Beat Error의 조건부 경고 배너는 항상 보이는 헤더 스트립의 왼쪽 예약 칸에 들어가고 오른쪽의 고정 버튼 영역이 헤더 높이를 유지하므로 경고 표시/해제 때 플롯 `*` 행 높이가 변하지 않는다. Long-Term 그래프는 Beat Error/Rate 그래프와 같은 X-only 입력 계약을 적용해 기본 ScottPlot 휠/드래그 줌이 Y축을 먼저 확대·축소한 뒤 렌더러 자동 보정으로 돌아오는 중간 렌더를 없애고, 자체 휠 처리는 공유 X창만 바꾸며 과확대 방지를 위해 X창 하한을 10초로 둔다. Long-Term Reset View도 Trace와 같은 상단 `*,Auto` 헤더 스트립의 우측 끝 버튼으로 배치해 플롯 내부와 리뷰 바를 가리지 않는다. 스택 그래프의 데이터 높이는 시간축을 가진 하단 패널만큼 해당 행을 키워(`InfoTabRegistry`의 row 비율 1.22*/1.11*) 맞춘다 — 단 설계창(1280×750) 기준 비율이라 극단적 리사이즈에서 수~수십 px 드리프트가 남는다(정직 표기). 표준 SAP 택틱명에 정확히 대응하지 않는 UI 설계 결정이라 △ | `RateScopeRenderer.cs`, `BeatErrorDiagRenderer.cs`, `AveragePeriodRateAnnotations.cs`, `TraceDisplayRenderer.cs`, `LongTermPerfRenderer.cs`, `InfoTabRegistry.cs` | △ |
| **defer binding** (Portability) | RID(예: `win-x64`/`linux-arm64`; 전체 `win-x64;win-arm64;linux-x64;linux-arm64`)에 따라 Platform 참조·`DefineConstants`를 조건부로 바인딩 → 같은 소스로 OS별 앱 생성 | `TimeGrapher.App.csproj` | △ |
| **limit dependencies — 소프트웨어 3D 렌더** (Portability) | Positions 탭의 워치 자세 표시를 GPU/OpenGL·외부 3D 라이브러리 없이 `System.Numerics` + Avalonia `WriteableBitmap`만으로 CPU 래스터화(원근 투영 + z-buffer + flat 양면 Lambert). 번들된 vertex-color GLB(`Assets/Model`)를 자체 최소 glTF 파서로 읽어 **새 네이티브/패키지 의존 0**으로 모든 타깃(Windows/Linux/Pi)·헤드리스에서 동일 동작(헤드리스 렌더 테스트가 잠금). 포지션 버튼 입력만으로 목표 자세를 `Quaternion.Slerp`(cubic ease-in-out, 650 ms — `watch_positions.json` 권고값)로 보간하며, 드래그 등 다른 입력은 없다. 모델 ≈3.5k 삼각형·뷰포트가 작아 CPU 래스터로 충분(애니메이션 중에만 타이머 가동, 정지 시 0) | `WatchModelView.cs`, `WatchModelRasterizer.cs`, `GlbMeshLoader.cs`, `WatchModelOrientation.cs` | ✓ |

---

## 2. Design Patterns

| Pattern | 적용 방식 | 근거 | |
|---|---|---|---|
| **Layers** | App / Platform.* / Core 3계층, 하향 의존만 + CI 강제 | `TimeGrapher.App.csproj` | ✓ |
| **Adapter** | `AudioCaptureWorker`가 NAudio를, `LinuxLiveAudioWorker`가 pw-record/arecord를 `ILiveAudioWorker`로 변환 | Platform.* | ✓(Win) / △(Linux: 프로세스 오케스트레이션 성격) |
| **Factory** | `LiveAudioBackend.CreateWorker`, `IRecordingWriterFactory`, `InfoTabRegistry`(kind→factory 딕셔너리) | 다수 | ✓ |
| **Strategy** | 탭별 frame consumer `IAnalysisFrameConsumer`를 `TabId`로 선택하고, 입력 모드 `IAudioInputWorker`를 동일하게 구동. 항상 보이는 포지션 버튼 strip은 `ObserveFrame`, Positions 시퀀스 표는 활성 탭 `RenderFrame`에서 같은 snapshot을 읽어 각 표시 비용을 분리한다 | `IAnalysisFrameConsumer.cs`, `WatchPositionsFrameConsumer.cs` | ✓ |
| **State** | run lifecycle 명령(`Start`, `Play/Pause`, `Reset`, stop retry)과 내부 stop-without-reset/device-refresh 전이를 상태 객체(`StoppedState`, `StartingState`, `RunningState`, `PausedState`, `StoppingState`, `StopFailedState`)에 위임해 상태별 허용 동작과 복구 전이를 한 곳에 둔다 | `RunCommandService.States.cs` | ✓ |
| **Command** | `RelayCommand`/`AsyncRelayCommand`(ICommand) — 재진입 차단 + CanExecute 재질의 | `AsyncRelayCommand.cs` | ✓ |
| **Observer** | 워커 이벤트(`AnalysisFrameReady`는 분석 워커, `DataReady`는 입력 워커, `CaptureEnded`는 라이브 워커) 구독·정지 시 해제 | `AnalysisWorker.cs`, `IAudioInputWorker.cs`, `ILiveAudioWorker.cs` | ✓ (브로커형 Pub-Sub은 아님) |
| **Producer-Consumer (bounded)** | 분석→`WavWriter` 스레드를 `BlockingCollection`으로 분리 | `QueuedWavStreamWriter.cs` | ✓ |
| **Shared-Data** | `MasterAudioBuffer`(단일 writer/reader 동기화 링버퍼) | `MasterAudioBuffer.cs` | ✓ (Blackboard 아님) |
| **MVVM** | VM이 바인딩 상태·ICommand 보유, XAML은 로직 없음 | `MainWindowViewModel.cs` | △ |
| **Pipe-and-Filter** | HPF→Envelope→Detector 단계형 데이터플로(엔벨로프 `_bufEnv`는 동시에 50 ms Delay 라인 `_bufEnvOut`으로 분기해 표시용 `ProcessedPcm` 정렬; Detector는 비지연 `_bufEnv`를 직접 소비) | `TgDetector.cs` | △ |
| **Map-Reduce** | — | — | ✗ 기각 |

---

## 3. 적용도에 대한 정직한 평가 (채점 포인트)

검증 단계에서 **단어만 비슷한 과잉 주장**을 다음과 같이 교정했다. 이 구분 자체가 SAP 학습의 핵심이다.

- **MVVM (△):** 바인딩·커맨드는 진짜지만, **시작/중지 생명주기가 아직 code-behind**(`MainWindow.RunLifecycle.cs`)에 있고 서비스가 VM 상태를 직접 변경한다 → "실용적 부분 MVVM". 발표 자료도 이를 인정.
- **Pipe-and-Filter (△):** 단계 구조는 맞지만 **단일 스레드 동기 호출 체인**이다. 진짜 동시 파이프 경계는 두 곳뿐 — `입력→링버퍼→분석`, `분석→녹음 큐`.
- **defer binding (△):** RID **빌드/배포 시점** 바인딩이라, 교과서가 강조하는 런타임 플러그인/지연 로딩(가장 늦은 바인딩)은 아니다. 카탈로그에서 가장 약한 바인딩 시점.
- **Map-Reduce (✗ 기각):** 분할·병렬·셔플이 전혀 없는 **증분 슬라이딩-윈도우 집계**일 뿐 → `reduce overhead` tactic으로 봐야 한다.
- **기타 교정:**
  - "bound execution times"는 과거 정지 join의 **대기 상한(2초)**뿐이었다. 이후 `AnalysisDeadlineMonitor`가 비트 주기 기반 백로그 감시와 점진 저하를 추가했다 — 단일 패스의 시간 상한은 여전히 없고(분석 블록 청크(기본 4096샘플)로 작업 **양**만 바운드), 마감은 사후 감시 + 저하로 다룬다.
  - stop "retry"는 자동 반복이 아니라 **복구 상태에서 사용자가 `RESET`을 다시 누르면 멱등 재시도**하는 구조다.
  - PipeWire→ALSA는 fault-recovery `reconfiguration`이 아니라 장치 열거/선택 단계의 **`degradation` 폴백**이다.
  - stale 콜백 폐기는 `ignore faulty behavior`가 아니라 `timestamp`의 stale 탐지 절반이다.

---

## 4. 가장 인상적인 설계 3가지 (발표 권장)

1. **CI로 강제되는 의존성 경계** — 아키텍처 규칙을 문서가 아닌 *실패하는 테스트*로 못박았다(architecture fitness function). `.github/workflows/ci.yml`이 Core의 OS 의존을 grep으로 차단하고, OS별 산출물에 잘못된 DLL이 섞이면 실패시킨다.
2. **"최신 프레임만" 렌더 + 활성 탭만 렌더** — 실시간 UI 멈춤을 막는 성능 tactic 집합(`limit event response` + `schedule resources` + `reduce overhead`). pause 종료 또는 Long-Term 탭 이탈 시 리뷰 커서 제거를 위한 저장 프레임 재렌더만 명시적 예외다.
3. **단조 run-session token** — 실시간 시작/중지의 stale-response 버그를 구조적으로 차단한다(`timestamp` tactic).

---

## 5. 원본(Qt/C++) 대비 성능 tactic·패턴 비교

> 2026-06-09, 원본 Qt/C++ TimeGrapher와 이 포팅본을 **비트당 125 ms 예산** 관점에서
> 양쪽 코드 file:line 대조로 분석한 결과다(주장 27건 전수 검증). 비교에서 발견된
> 포팅 회귀 6건은 `perf(imaging)`/`perf(analysis)`x2/`perf(detection)`/`perf(shared)`/
> `perf(rendering)` 커밋으로 수정했고, 마감 강제는 `feat(analysis)` 커밋으로
> 추가했다. **아래 표는 수정 반영 후 상태다.** 이 절의 `MainWindow.cpp` 등
> `*.cpp` file:line 인용은 **외부 원본 Qt/C++ 소스**의 위치이며 이 저장소에는
> 없다(C# 포팅본의 메인 윈도우는 `MainWindow.axaml.cs`).

### 5.1 SEI 성능 tactic 비교

| SEI Tactic | 원본 (Qt/C++) | 포팅본 (C#) |
|---|---|---|
| **introduce concurrency** | 부분 — 캡처만 별도 QThread. 검출 + 플롯 + WAV 쓰기는 전부 GUI 스레드(`MainWindow.cpp:901-1027`) → UI 지연이 곧 검출 지연 | **완전** — AnalysisWorker 전용 스레드(Highest) + WavWriter 스레드 + UI 3계층. **포팅 최대 개선**: UI가 느려져도 검출은 영향 없음 |
| **limit event response** | 페인트 합류만(QCustomPlot `rpQueuedReplot`) | 단일 슬롯 latest-wins 프레임 합류 + 탭별 33/100 ms 스로틀 + 일회성 신호(오버런)는 병합으로 보존(`AnalysisFrameRenderScheduler.cs`) |
| **bound resource usage** | 그래프가 10초 = 48만 포인트 보유, 매초 50–100회 **전체 컨테이너 rescale**, ~5초마다 24만 포인트 purge 스파이크 | 생산 측에서 32000/250 포인트로 데시메이션 — 원본의 최악 비용 2개가 구조적으로 소멸. **1시간째 비용 = 1초째 비용** |
| **bound queue sizes** | 링은 무음 랩 — 드롭 계측 없음, 30초 이상 밀리면 손상된 타임라인을 감지 없이 읽음 | 모든 버퍼 바운드 + 드롭 정책 명시·계측(링 / WAV 큐 128 / 렌더 큐 1) |
| **schedule resources** | Windows에서 프로세스 전체 `REALTIME_PRIORITY_CLASS` + `timeBeginPeriod(1)` (포팅본엔 없음) | 스레드 우선순위 사다리 + **일반 프레임은 활성 탭만 렌더링**(Strategy 라우팅). pause 종료 또는 Long-Term 탭 이탈의 커서 제거 때만 `RenderToAll`이 저장 프레임을 1회 재렌더한다. ⚠ .NET `Thread.Priority`는 **Linux에서 no-op** — Pi에서 우선순위는 무효과이며 실제 보호 장치는 바운드 큐 구조다 |
| **increase resource efficiency** | O(1) 증분 알고리즘(RLS graph rate, Avg. Period 상단바 구간 누적기, PLL ~30 flops/event) | 동일하게 충실 포팅 — 세션 길이와 무관하게 비트당 비용 평탄. 마커→컬럼 조회도 O(1)로 개선(원본은 양쪽 다 O(폭) 선형 탐색이던 핫스팟) |
| **bound execution times** | 알고리즘 캡만(C-onset 탐색 ~5 ms 등). 스냅샷 바운드 드레인은 원본에도 있음 | 동일 + 분석 블록(기본 4096샘플)마다 stop 체크 + `ProcessingElapsedMs` 측정 + **`AnalysisDeadlineMonitor`가 백로그를 비트 주기로 환산해 점진 저하 실행**(위 "실시간 마감 예산" 절) |

### 5.2 성능 관련 디자인 패턴 비교

| 패턴 | 비교 |
|---|---|
| **Producer-Consumer (30초 공유 링)** | 양쪽의 하중 지지 패턴. 포팅본이 추가한 것: 오버런 드롭 계측, 드레인 중 중단 가능, 지연 텔레메트리 |
| **Producer-Consumer (WAV 기록, bounded)** | **원본에 없음** — GUI 스레드에서 동기 블로킹 파일 I/O(`MainWindow.cpp:926`). SD카드 fsync 지연이 비트 예산을 직접 잠식. 포팅본은 ArrayPool + BlockingCollection(128) drop-never-block, 큐 깊이가 디스크 지연 ~10.9초 흡수 |
| **Observer (queued) + 세션 게이팅** | 양쪽 다 알림에 페이로드를 싣지 않음(데이터는 링으로만). 포팅본은 SessionId + 세대 카운터 3중 게이팅으로 이전 런의 늦은 프레임이 새 런 예산을 0 소비 |
| **Active Object / Mediator / Guarded Suspension** | 포팅본이 더 명시적(`RunSessionController`, `WorkerPauseGate` 50 ms 슬라이스) — 핫 상태 단일 라이터 보장, 정지 경로가 UI를 못 잠그게 함 |
| **Pipes-and-Filters (DSP 체인)** | 양쪽 동일한 포크 토폴로지(검출기는 지연 안 된 envelope을 읽음). 비트당 코어의 ~0.05–0.2% — 병렬화 불필요, 동기 체인 유지가 C 원본 대비 검증성 보존 |
| **Double Buffering** | 원본: 동일 스레드라 QImage 하나로 무복사. 포팅본: 스레드가 분리되어 발행 스냅샷이 필요 — **고정 3버퍼 풀 로테이션**으로 정상 상태 할당 0(UI 측은 WriteableBitmap + 스크래치 재사용) |
