# Core Signal Processing and Warning Flow

이 문서는 `TimeGrapher.Core.Detection`과 `TimeGrapher.Core.Analysis`가 오디오 샘플을 BPH lock, post-lock 추적, 메트릭, `AnalysisFrame`으로 바꾸는 흐름과 App이 그 데이터를 받아 경고 UI를 표시하는 흐름을 요약한다. Mermaid 다이어그램은 실제 코드 경계에 맞춰 작성했다.

주요 코드 위치:

- `src/TimeGrapher.Core/Detection/TgDetector.cs`
- `src/TimeGrapher.Core/Detection/Detector.cs`
- `src/TimeGrapher.Core/Detection/Bph.cs`
- `src/TimeGrapher.Core/Analysis/DetectorMetricsEngine.cs`
- `src/TimeGrapher.Core/Analysis/AnalysisWorker.cs`
- `src/TimeGrapher.App/Services/AnalysisFramePresenter.cs`
- `src/TimeGrapher.App/Services/AnalysisRunStatusReporter.cs`
- `src/TimeGrapher.App/Rendering/TraceAlertEvaluator.cs`
- `src/TimeGrapher.App/Rendering/BeatErrorDiagnostics.cs`

## 1. Core 전체 파이프라인

```mermaid
flowchart TD
    source["Live / Playback / Simulation input worker"] --> buffer["MasterAudioBuffer<br/>30 s mono float ring buffer"]
    buffer --> notify["DataReady -> AnalysisWorker.NotifyDataReady()"]
    notify --> worker["AnalysisWorker thread<br/>HandleInputDataCore"]

    worker --> copy["CopyAnalysisSamples()<br/>analysis block"]
    copy --> sideProj["Image / filter pre-processing<br/>SoundPrint, Spectrogram, MultiFilter"]
    copy --> engine["DetectorMetricsEngine.Process(block)"]

    engine --> detector["TgDetector.Process"]
    detector --> dsp["HPF -> envelope -> 50 ms delay line"]
    dsp --> raw["TgDetectorCore<br/>silence/burst detector emits raw A/C"]
    raw --> lockPath["BPH acquisition / sync tracking"]
    lockPath --> emit["Public TgEvent list<br/>A/C timing, C-onset metadata"]

    emit --> metrics["WatchMetrics<br/>rate, beat error, amplitude, derived measures"]
    emit --> quality["Optional SignalQualityFeatureExtractor + classifier<br/>advisory only"]
    metrics --> update["DetectorMetricsBlockUpdate<br/>DetectorResultSnapshot + DetectedEventUpdate[]"]
    quality --> update

    update --> projectors["Analysis projectors"]
    projectors --> scope["ScopeRateFrameProjector"]
    projectors --> history["BeatMetricsFrameProjector<br/>BeatMetricsHistorySnapshot"]
    projectors --> segments["BeatSegmentCapture<br/>BeatSegmentsSnapshot + quality flags"]
    projectors --> sweep["SweepFrameProjector"]
    projectors --> filters["MultiFilterFrameProjector"]
    projectors --> images["SoundPrint / Spectrogram snapshots"]

    scope --> frame["AnalysisFrame"]
    history --> frame
    segments --> frame
    sweep --> frame
    filters --> frame
    images --> frame
    worker --> diag["Deadline / latency / missed-beat diagnostics"]
    diag --> frame
    frame --> ready["AnalysisWorker.AnalysisFrameReady(frame)"]
```

`DetectorMetricsEngine`가 Detection과 Metrics의 공유 경계다. `TgDetector`는 raw A/C event와 sync 상태를 만들고, `WatchMetrics`는 lock된 BPH와 A/C event를 이용해 Error Rate, Amplitude, Beat Error, Avg. Period 구간을 계산한다. `AnalysisWorker`는 같은 block update를 여러 projector에 fan-out해서 한 번의 UI 갱신 단위인 `AnalysisFrame`으로 합친다.

## 2. BPH acquisition, manual BPH, lock 이후 처리

```mermaid
flowchart TD
    raw["Raw A/C events from TgDetectorCore"] --> prelock{"_sync.Synced == 0?"}

    prelock -->|yes| acqGate["Pre-lock acquisition gate<br/>AcquisitionPeakGateFraction<br/>weak half-beat artifact rejection"]
    acqGate --> pushA["Push A event times into history"]
    pushA --> enough{"A history >= 8<br/>and span >= AutoDetectSeconds?"}

    enough -->|no| notSynced["NotSynced<br/>continue collecting"]
    enough -->|yes| mode{"BphMode"}

    mode -->|Auto| autoPick["Bph.PickByPhase()<br/>score common AutoBphList candidates"]
    mode -->|Manual| manualScore["Bph.PhaseScore()<br/>score only configured ManualBph period"]

    autoPick --> score{"phase score >= 0.7?"}
    manualScore --> score
    score -->|no| mismatch["No lock<br/>manual mode reports Mismatch after detect attempt"]
    score -->|yes| lock["_sync.Lock(bph, period, acOffset, tolerance)"]

    lock --> tighten["Post-lock detector tuning<br/>MinSilence = 0.4T<br/>MinAInterval = 0.7T<br/>CSearchSkip = 0.03T"]
    tighten --> synced["SyncStatus = Synced<br/>DetectedBph set"]

    prelock -->|no| phaseGuide["SetPhaseGuide(next A, period, A-C offset, window, onset scale)"]
    phaseGuide --> rescue["Weak-A onset rescue<br/>PhaseGuideOnsetRescueScale lowers onset threshold near expected phase"]
    rescue --> detectGuided["Guided A/C detection"]
    detectGuided --> syncUpdate["TgSync.Update(raw event time)<br/>PLL-ish phase tracking"]
    syncUpdate --> watchdog{"Consecutive misses<br/>or time watchdog exceeded?"}
    watchdog -->|no| emitSynced["Emit synced events"]
    watchdog -->|yes| syncLost["SyncLostEvent<br/>clear current BPH/history<br/>restore pre-lock gates"]
    syncLost --> notSynced
```

Manual BPH는 "즉시 lock"이 아니다. Auto 후보 목록 선택만 건너뛰고, 사용자가 지정한 BPH 하나에 대해 phase score를 계산한다. 충분한 A-event history가 쌓이고 score가 0.7 이상일 때만 `_sync.Lock(...)`이 호출된다.

Lock 이후에는 다음 변화가 생긴다.

- detector가 BPH period를 알기 때문에 silence gate와 A-to-A minimum interval을 더 강하게 제한한다.
- C-search skip이 beat period의 약 3%로 조정된다.
- 다음 block부터 `SetPhaseGuide(...)`가 예상 A phase 주변 window를 전달한다.
- `Weak-A onset rescue`는 별도 후처리 단계가 아니라 phase guide 안에서 onset threshold scale을 바꾸는 lock-aware detection 보정이다.
- sync loss가 발생하면 current BPH, event history, sync state를 지우고 pre-lock gate 값으로 되돌아간다.

## 3. A/C event에서 메트릭과 프레임으로 가는 흐름

```mermaid
flowchart TD
    events["TgEvent A/C list"] --> eventLoop["DetectorMetricsEngine.BuildUpdate"]
    eventLoop --> aevent{"TgEventType"}

    aevent -->|A| handleA["WatchMetrics.HandleAEvent"]
    aevent -->|C| handleC["WatchMetrics.HandleCEvent"]

    handleA --> rate["Rolling rate / Error Rate<br/>RLS graph rate and display rate"]
    handleA --> beatError["Beat Error / tic-toc phase measures"]
    handleC --> amplitude["A->C amplitude<br/>lift angle + onset-latency compensation"]

    rate --> metricsUpdate["WatchMetricsUpdate"]
    beatError --> metricsUpdate
    amplitude --> metricsUpdate
    metricsUpdate --> blockUpdate["DetectedEventUpdate[]"]

    eventLoop --> qualityWindow{"Quality classifier injected?"}
    qualityWindow -->|yes| features["SignalQualityFeatureExtractor<br/>SNR, peak margin, jitter, missed/sync rates"]
    features --> classify["ISignalQualityClassifier.Classify"]
    classify --> qa["SignalQualityAssessment<br/>advisory; does not drop events"]
    qualityWindow -->|no| noQa["QualityAssessment = null"]

    blockUpdate --> snapshot["DetectorResultSnapshot"]
    qa --> snapshot
    noQa --> snapshot

    snapshot --> frameProject["AnalysisWorker projectors"]
    blockUpdate --> frameProject
    frameProject --> history["BeatMetricsHistorySnapshot<br/>Rate / Amplitude / Beat Error series and stats"]
    frameProject --> beatSegments["BeatSegmentsSnapshot<br/>recent beat windows and SignalQualityFlags"]
    frameProject --> frame["AnalysisFrame<br/>metrics, graphs, images, diagnostics"]
    history --> frame
    beatSegments --> frame
```

`SignalQualityAssessment`와 `SignalQualityFlags`는 trust/warning annotation이다. 이 경로는 검출된 event나 metric을 제거하지 않는다. Per-beat geometry 경고는 `BeatSegmentCapture`가 `WeakSignal`, `NoisySignal`, `CTimingUnstable`, `PossibleFalseC`, `ClippedSignal` 같은 flags로 싣고, optional classifier는 window-level `SignalQualityAssessment`를 싣는다.

## 4. App으로 넘어간 뒤 경고 UI가 뜨는 흐름

```mermaid
sequenceDiagram
    participant AW as AnalysisWorker<br/>(analysis thread)
    participant S as AnalysisFrameRenderScheduler
    participant UI as MainWindow<br/>(UI thread)
    participant R as GraphFrameRenderer / AnalysisFrameRouter
    participant P as AnalysisFramePresenter
    participant VM as MainWindowViewModel
    participant T as Trace / Beat Error renderers

    AW->>S: AnalysisFrameReady(frame)
    S->>S: latest-wins queue<br/>merge transient signals
    S->>UI: Dispatcher.UIThread.Post(HandleAnalysisFrame)
    UI->>UI: discard if SessionId is stale
    UI->>R: UpdateResults(frame)
    UI->>R: Route(frame, activeTab, context)
    R->>T: active tab RenderFrame(frame)
    UI->>P: Present(frame, droppedFrames, displayTicks, sampleRate)
    P->>VM: StatusText or SetWarningStatus(...)
```

경고 UI는 크게 두 계열이다.

```mermaid
flowchart TD
    frame["AnalysisFrame"] --> statusReporter["AnalysisRunStatusReporter.Describe"]
    statusReporter --> statusChecks{"Status warning priority"}
    statusChecks --> overrun["InputOverrun<br/>Audio input interrupted"]
    statusChecks --> lag["AnalysisLagSamples > sampleRate/4<br/>Analysis running behind"]
    statusChecks --> deadline["DeadlineDegradationLevel > 0<br/>Display quality reduced"]
    statusChecks --> quality["BeatSegments.Quality OR SignalQualityFlagsMap(frame.SignalQuality)"]
    statusChecks --> nosignal["No beat after grace period<br/>No signal"]
    overrun --> vmWarn["MainWindowViewModel.SetWarningStatus"]
    lag --> vmWarn
    deadline --> vmWarn
    quality --> vmWarn
    nosignal --> vmWarn
    vmWarn --> statusBar["Status bar warning style<br/>and user error log"]

    frame --> history{"frame.MetricsHistory present?"}
    history --> traceEval["TraceAlertEvaluator.Evaluate(history)"]
    traceEval --> traceBanner{"Message?"}
    traceBanner -->|yes| traceUi["Trace tab alert banner<br/>late rate / amplitude out of range"]

    history --> beatDiag["BeatErrorDiagnostics.Evaluate(history)"]
    beatDiag --> beatBanner{"Message?"}
    beatBanner -->|yes| beatUi["Beat Error tab alert banner<br/>major fault / separation alert"]
```

상태바 경고는 active tab과 무관하게 `AnalysisFramePresenter`가 처리한다. 반면 Trace tab과 Beat Error tab의 alert banner는 해당 tab renderer가 `BeatMetricsHistorySnapshot`을 평가해서 표시한다. 둘 다 Core가 만든 같은 frame/history 데이터를 읽으므로 그래프 값과 경고 문구가 같은 source를 공유한다.

## 5. 경고별 데이터 출처

| 경고 UI | 평가 위치 | Core/App 데이터 | 조건 요약 |
|---|---|---|---|
| Status bar: audio interrupted | `AnalysisRunStatusReporter` | `AnalysisFrame.InputOverrun`, `InputSamplesDropped` | 입력 ring buffer overrun |
| Status bar: analysis behind | `AnalysisRunStatusReporter` | `AnalysisFrame.AnalysisLagSamples`, `ProcessingElapsedMs` | lag가 sample rate의 1/4초 초과 |
| Status bar: reduced quality | `AnalysisRunStatusReporter` | `AnalysisFrame.DeadlineDegradationLevel` | deadline monitor가 display quality degradation 적용 |
| Status bar: signal quality | `AnalysisRunStatusReporter` | `BeatSegmentsSnapshot.Quality`, `AnalysisFrame.SignalQuality` | per-beat flags와 classifier verdict를 OR |
| Status bar: no signal | `AnalysisRunStatusReporter` | `BeatSynced`, `BeatSegments`, `GraphTickEnd` | 일정 시간 beat/sync/segment 없음 |
| Trace tab banner | `TraceAlertEvaluator` | `BeatMetricsHistorySnapshot.RateSPerDay`, `AmplitudeDeg` | late-running 또는 amplitude 정상 밴드 이탈 |
| Beat Error tab banner | `BeatErrorDiagnostics` | `BeatMetricsHistorySnapshot.BeatErrorSignedMs`, `RateSPerDay`, `Bph` | tic/toc separation 초과 또는 slope major fault |

## 6. 책임 경계

```mermaid
flowchart LR
    detection["Core.Detection<br/>signal -> A/C events + sync/BPH"] --> analysis["Core.Analysis<br/>events -> metrics/projector snapshots"]
    analysis --> shared["Core.Shared DTOs<br/>AnalysisFrame, BeatMetricsHistorySnapshot, BeatSegmentsSnapshot"]
    shared --> app["TimeGrapher.App<br/>routing, rendering, status/warning UI"]

    app -. "does not call Detection directly for UI warnings" .-> shared
    detection -. "no Avalonia/UI dependency" .-> shared
    analysis -. "no Avalonia/UI dependency" .-> shared
```

Core는 UI를 모른다. 경고창/경고 배너 여부는 App이 `AnalysisFrame`과 그 안의 snapshot DTO를 읽어서 결정한다. Core 쪽 책임은 신호처리, BPH/sync 상태, metric, quality annotation을 UI-independent DTO로 내보내는 데서 끝난다.
