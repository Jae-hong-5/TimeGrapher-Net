# Signal Quality 테스트 가이드

이 문서는 `feature/signal-quality-propagation` 브랜치의 stage 1, stage 2,
그리고 그래프 overlay signal-quality 변경사항을 검증하기 위한 가이드다.

## 변경 내용

앱은 이제 beat-segment 공유 DTO를 통해 signal-quality warning을 전달한다.
현재 사용하는 flag는 다음과 같다.

- `WeakSignal`: 표시 중인 beat segment에서 사용할 수 있는 C marker를 찾지 못했다.
- `NoisySignal`: 감지된 C timing이 최근 A-to-C timing과 일관되지 않는다.
- `CTimingUnstable`: A-to-C interval이 최근 median/MAD band에서 벗어났다.
- `PossibleFalseC`: C 후보가 비정상적으로 이르며 B/noise가 C로 잡혔을 가능성이 있다.
- `ClippedSignal`: clipping classification을 위해 예약된 flag다.
- `NoSignal`: no-signal classification을 위해 예약된 flag다.

Stage 1에서는 공유 quality 상태를 상단 readout과 Beat Noise에 표시했다.
Stage 2에서는 recovery guidance를 추가하고, 의심스러운 C 후보를 amplitude 갱신에서
제외하며, quality 상태를 beat-aligned analysis view까지 전달했다. 이번 graph-overlay
변경에서는 Beat Noise, Waveform Compare, Escapement Analyzer의 우측 상단에 warning
overlay를 추가하여 사용자가 현재 보고 있는 diagnostic view 안에서 바로 경고를 볼 수
있게 했다.

## Architecture View: `AnalysisFrame.BeatSegments` 전달 구조

이 섹션은 signal-quality 평가값이 어디에 저장되고 어떤 경로로 각 그래프에 전달되는지 보여준다. 핵심은 `Core`가 품질을 판단하고, `Core.Shared` DTO인 `BeatSegmentsSnapshot`을 `AnalysisFrame.BeatSegments`에 실어 `App`으로 전달하며, App의 renderer와 service는 같은 DTO를 읽어 표시만 담당한다는 점이다.

### Uses View

이 뷰는 compile-time/code-level 책임 분리를 설명한다. 실선 `A --> B`는 A가 B를 직접 사용·소유·호출한다는 뜻이고, 점선 `A -.-> B`는 이미 전달받은 `frame.BeatSegments` DTO를 읽는 소비 관계를 뜻한다. 점선은 callback, ownership, 역방향 runtime dependency를 의미하지 않는다.

![AnalysisFrame.BeatSegments Uses View](assets/signal-quality-beatsegments-uses.svg)

```mermaid
flowchart TB
    subgraph Core["TimeGrapher.Core"]
        AnalysisWorker["AnalysisWorker<br/>frame 생성·발행"]
        BeatSegmentCapture["BeatSegmentCapture<br/>beat segment 수집·품질 분류"]
        WatchMetrics["WatchMetrics<br/>의심 C amplitude 갱신 제외"]
    end

    subgraph Shared["TimeGrapher.Core.Shared"]
        AnalysisFrame["AnalysisFrame<br/>BeatSegments field"]
        BeatSegmentsSnapshot["BeatSegmentsSnapshot<br/>Version · Segments · Markers · Average · Quality"]
        BeatSegment["BeatSegment<br/>per-beat Quality"]
        SignalQualityFlags["SignalQualityFlags<br/>Weak · Noisy · CTimingUnstable · PossibleFalseC"]
    end

    subgraph App["TimeGrapher.App"]
        MainWindow["MainWindow<br/>OnAnalysisFrameReady / HandleAnalysisFrame"]
        FrameRouter["AnalysisFrameRouter<br/>Observe all · Render active tab"]
        GraphFrameRenderer["GraphFrameRenderer<br/>top readout Signal suffix"]
        StatusReporter["AnalysisRunStatusReporter<br/>recovery guidance"]
        BeatNoise["BeatNoiseScopeRenderer"]
        WaveformCompare["WaveformCompareRenderer"]
        Escapement["EscapementAnalyzerRenderer"]
        OtherConsumers["Scope/Spectrogram/Filter consumers<br/>segments·markers 해석 보조"]
        SignalText["SignalQualityText / SignalQualityOverlayState"]
    end

    AnalysisWorker --> BeatSegmentCapture
    BeatSegmentCapture --> AnalysisFrame
    BeatSegmentCapture --> BeatSegmentsSnapshot
    BeatSegmentsSnapshot --> BeatSegment
    BeatSegmentsSnapshot --> SignalQualityFlags
    BeatSegment --> SignalQualityFlags
    WatchMetrics --> SignalQualityFlags

    MainWindow --> AnalysisFrame
    MainWindow --> FrameRouter
    MainWindow --> GraphFrameRenderer
    MainWindow --> StatusReporter
    FrameRouter --> BeatNoise
    FrameRouter --> WaveformCompare
    FrameRouter --> Escapement
    FrameRouter --> OtherConsumers

    GraphFrameRenderer --> SignalText
    StatusReporter --> SignalText
    BeatNoise --> SignalText
    WaveformCompare --> SignalText
    Escapement --> SignalText

    GraphFrameRenderer -.-> BeatSegmentsSnapshot
    StatusReporter -.-> BeatSegmentsSnapshot
    BeatNoise -.-> BeatSegmentsSnapshot
    WaveformCompare -.-> BeatSegmentsSnapshot
    Escapement -.-> BeatSegmentsSnapshot
    OtherConsumers -.-> BeatSegmentsSnapshot
```

정적 책임은 다음과 같이 나뉜다.

- `BeatSegmentCapture`는 beat window를 만들고 `ClassifyQuality()`로 `WeakSignal`, `CTimingUnstable`, `NoisySignal`, `PossibleFalseC`를 결정한다.
- 개별 beat의 품질은 `BeatSegment.Quality`에 저장된다.
- 최근 beat ring 전체의 품질은 `BeatSegmentsSnapshot.Quality`에 OR 집계되어 저장된다.
- `AnalysisFrame.BeatSegments`는 이 snapshot을 App으로 넘기는 단일 전달 슬롯이다.
- App의 readout, status, graph renderer는 같은 `BeatSegmentsSnapshot`을 읽고 `SignalQualityText`/`SignalQualityOverlayState`로 문구와 overlay만 만든다.

### Runtime Sequence View

이 뷰는 구현 메서드 호출을 모두 펼치지 않고, signal-quality 정보가 Core에서 판단되어 `AnalysisFrame.BeatSegments` DTO로 App 표시 계층까지 전달되는 큰 흐름만 보여준다.

![AnalysisFrame.BeatSegments Runtime Sequence View](assets/signal-quality-beatsegments-sequence.svg)

```mermaid
sequenceDiagram
    autonumber
    participant Metrics as Detector metrics
    participant Core as Core analysis
    participant Frame as AnalysisFrame
    participant Ui as UI scheduler
    participant Presentation as Readout / Status / Graphs

    Metrics->>Core: A/C event metrics arrive
    Core->>Core: classify beat quality and aggregate flags
    Core->>Frame: attach BeatSegmentsSnapshot to BeatSegments
    Note over Core,Frame: AnalysisFrame.BeatSegments is the single DTO handoff point
    Frame->>Ui: publish latest analysis frame
    Ui->>Presentation: render using the same BeatSegments DTO
```

전달 경로에서 중요한 점은 다음과 같다.

- `Core`는 beat 품질을 판단하고 최근 beat ring의 품질을 `BeatSegmentsSnapshot.Quality`로 집계한다.
- `AnalysisFrame.BeatSegments`는 Core에서 App으로 signal-quality 상태를 넘기는 단일 DTO 슬롯이다.
- App의 readout, status guidance, graph overlay는 별도 이벤트 버스나 역참조 없이 같은 `BeatSegmentsSnapshot`을 읽어 표시만 담당한다.

## 프로젝트 플랜 기반 그래프별 비정상 신호 안내 체크리스트

프로젝트 플랜은 TimeGrapher가 단순히 값을 보여주는 것이 아니라, 약한 신호, 잡음, 누락, clipping, 잘못 잡힌 이벤트처럼 측정값을 오해하게 만들 수 있는 조건을 사용자에게 알려야 한다고 요구한다. 특히 "signal too noisy", "reposition watch", "microphone gain too high", "measurement confidence low" 같은 guidance와, weak/noisy/partially missing signal에서 불안정하거나 misleading한 출력을 내지 않는 graceful degradation이 핵심이다.

아래 체크리스트는 최종 데모와 수동 QA에서 그래프별로 확인할 항목이다. `[직접 경고]`는 현재 signal-quality warning/overlay가 직접 표시되어야 하는 항목이고, `[해석 보조]`는 그래프 자체의 reference line, label, range, marker, 비교 UI로 사용자가 비정상 가능성을 판단할 수 있어야 하는 항목이다.

| 그래프 / 표시 영역 | 프로젝트 플랜에서 요구한 비정상 신호 안내 관점 | 체크리스트 |
|---|---|---|
| 공통 상단 readout / status guidance | 입력이 noisy, weak, clipped, incomplete, misleading일 수 있음을 실시간으로 알려야 한다. | [ ] `Signal ...` suffix가 측정값 옆에 표시된다. [ ] status guidance가 reposition, gain 조정, handling/ambient noise 감소처럼 사용자가 취할 행동을 말한다. [ ] runtime 성능 저하 경고와 acoustic signal-quality 경고를 혼동하지 않는다. |
| Sound Graph / Sound Print | raw 또는 processed watch signal을 보여주고, 작은 timing fluctuation, averaging window, threshold/reference line을 통해 stability/noise/watch problem을 이해하게 해야 한다. | [ ] 잡음이 심하거나 신호가 약한 구간에서 사용자가 clean signal처럼 오해하지 않도록 warning 또는 guidance가 연결된다. [ ] averaging/filtering을 켠 경우 원신호의 약한 성분이 숨을 수 있음을 설명할 수 있다. [ ] pause/review 중에도 warning context가 사라지지 않는다. |
| Rate/Scope | 원 신호와 분석된 timing view를 비교할 수 있어야 하며, 같은 raw signal이 Sound Print와 일관되게 해석되어야 한다. | [ ] raw/processed view가 같은 입력 구간을 기준으로 설명된다. [ ] marker가 불안정하거나 C 후보가 의심스러울 때 상단 readout/status warning과 모순되지 않는다. |
| Trace Display | rate가 늦거나 amplitude가 270-300도 범위를 벗어나면 사용자에게 alert해야 한다. | [ ] rate late alert가 표시된다. [ ] amplitude out-of-range alert가 표시된다. [ ] smoothing 때문에 짧은 이상 구간이 완전히 숨지 않는지 설명할 수 있다. [ ] signal-quality warning이 있으면 trace 값을 확정 판정처럼 말하지 않는다. |
| Vario / Rate-Amplitude Stability | rate/amplitude의 min, max, average, sigma와 acceptable range를 구분해 장기 안정성 이상을 읽게 해야 한다. | [ ] acceptable range가 시각적으로 구분된다. [ ] min/max/average/sigma가 장기 불안정 또는 variation 증가를 드러낸다. [ ] 약신호/잡음 warning이 발생한 구간의 통계 해석에 주의가 필요함을 설명할 수 있다. |
| Multi-Position Sequence / Positions | 포지션별 rate, amplitude, beat error와 X/D summary로 자세별 불안정 또는 balance-wheel unbalance 가능성을 보여야 한다. | [ ] 각 포지션 결과가 active position과 연결된다. [ ] 포지션 간 차이가 큰 경우 신호 문제인지 실제 자세별 성능 차이인지 구분해 설명한다. [ ] weak/noisy warning이 있었던 포지션 결과를 clean 결과처럼 비교하지 않는다. |
| Beat Noise Scope | tick/tock beat noise의 shape, timing, repeatability를 summary measurement 대신 직접 검사하게 해야 한다. | [ ] [직접 경고] `WEAK SIGNAL`, `POSSIBLE FALSE C`, `C TIMING UNSTABLE` overlay가 Beat Noise graph area에 표시된다. [ ] Scope 1의 A/C marker가 의심스러운 C를 clean C처럼 보이게 하지 않는다. [ ] Scope 2 averaging이 random noise를 줄이는 목적임을 설명한다. [ ] 이전 beat strip 확대 보기에서도 warning context를 유지한다. |
| Beat Error Display / Diagnostic Trace | 숫자와 trace line이 일관되어야 하며, tick/tock line spacing 초과와 45도 이상 slope는 fault-state로 알려야 한다. | [ ] spacing acceptable range와 warning이 표시된다. [ ] trace slope가 과도할 때 fault-state indication이 있다. [ ] signal-quality warning이 있을 때 spacing/slope 판단을 확정 진단처럼 말하지 않는다. |
| Long-Term Performance Graph | rate, amplitude, beat error가 장기적으로 어떻게 변하는지 보여주고, variation range와 average로 안정성을 판단하게 해야 한다. | [ ] 장기 average와 variation range가 보인다. [ ] acceptable/reference range가 있으면 trace와 함께 읽힌다. [ ] warning이 발생한 구간이 장기 추세 해석을 오염시킬 수 있음을 설명할 수 있다. |
| Escapement Analyzer / Marker-Line Display | A/C timing marker와 ms label을 통해 fine-grained beat timing을 검사하고, onset/peak 같은 alternative reference가 더 안정적인지 비교하게 해야 한다. | [ ] [직접 경고] 우측 상단 overlay에 signal-quality warning이 표시된다. [ ] 불안정한 C marker를 정상 repeatability sample처럼 취급하지 않는다. [ ] marker position과 waveform feature가 어긋나 보이면 measurement confidence가 낮다는 guidance와 연결한다. |
| Time-Frequency Spectrogram | 시간-주파수 에너지 구조와 color intensity로 반복 beat pattern, 주요 acoustic component, frequency band behavior를 해석하게 해야 한다. | [ ] color scale/legend로 약한 에너지와 강한 에너지를 구분할 수 있다. [ ] 반복 구조가 흐리거나 외부 잡음 band가 강한 경우 noisy/low-confidence 상황으로 설명한다. [ ] spectrogram만으로 rate/amplitude를 확정하지 않고 다른 diagnostic view와 함께 해석한다. |
| Waveform Compare | aligned lanes에서 waveform shape, spacing, consistency를 비교하고 landmark를 식별해야 한다. | [ ] [직접 경고] 우측 상단 overlay와 lane label에 signal-quality warning이 표시된다. [ ] `PossibleFalseC` beat는 mean-C guide에서 제외된다. [ ] lane 간 shape/spacing inconsistency가 noise 또는 weak signal 가능성과 연결된다. |
| Scope Sweep | fixed sweep window에서 beat pattern이 안정적으로 머무는지, fast/slow일 때 drift가 나타나는지 보여야 한다. | [ ] pattern drift를 fast/slow 또는 sync 불안정과 구분해 설명한다. [ ] nominal reference 값이 있으면 현재 sweep과 비교한다. [ ] signal-quality warning이 있을 때 drift를 watch fault로 단정하지 않는다. |
| Filter Scope / F0-F3 | 같은 신호를 여러 filter view로 비교해 raw representation, smoothing, landmark emphasis, T1/T2/T3 식별을 도와야 한다. | [ ] F0는 closest raw representation으로 설명된다. [ ] F1 smoothing이 background noise를 줄이지만 low-amplitude component를 덜 보이게 할 수 있음을 표시/설명한다. [ ] F2/F3가 feature를 강조해도 원신호와 다른 해석일 수 있음을 설명한다. [ ] 네 filter view가 같은 input signal/time axis를 공유한다. |

데모에서는 모든 항목을 길게 보여주기보다, `공통 readout/status -> Beat Noise -> Waveform Compare -> Escapement Analyzer`를 signal-quality 직접 경고 경로로 보여주고, Trace/Beat Error/Long-Term/Vario/Scope/Filter 계열은 각 그래프의 reference line, range, marker, trend가 비정상 가능성을 어떻게 보조하는지 짧게 연결하면 된다.

## Overlay Fade 규칙

그래프가 clean하지 않은 `SignalQualityFlags` 값을 받으면 overlay는 가장 최근 warning을
불투명하게 표시한다. 이후 clean frame이 들어오면 마지막 warning을 clean update 10회 동안
그대로 유지하고, 그 다음부터 선형으로 희미해지다가 100번째 clean update에서 사라진다.

이 동작은 wall-clock 시간이 아니라 frame count 기반이다. 따라서 Playback, Live,
Simulation mode에서 같은 analysis-frame sequence가 들어오면 동일하게 동작한다.

## 자동 검증

전체 build를 실행한다.

```powershell
dotnet build TimeGrapherNet.sln -c Release
```

Core 관련 focused test를 실행한다.

```powershell
dotnet test tests/TimeGrapher.Core.Tests/TimeGrapher.Core.Tests.csproj -c Release --no-build --filter "BeatSegmentCaptureTests|WatchMetricsTests"
```

App 관련 focused test를 실행한다.

```powershell
dotnet test tests/TimeGrapher.App.Tests/TimeGrapher.App.Tests.csproj -c Release --no-build --filter "WaveformCompareLogicTests|AnalysisRunStatusReporterTests|SignalQualityTextTests"
```

이 브랜치에서 사용한 통합 focused suite를 실행한다.

```powershell
dotnet test TimeGrapherNet.sln -c Release --no-build --filter "SignalQualityTextTests|AnalysisRunStatusReporterTests|WaveformCompareLogicTests|BeatSegmentCaptureTests|WatchMetricsTests|BeatNoiseScopeRendererTests|Escapement"
```

전체 test suite를 실행한다.

```powershell
dotnet test TimeGrapherNet.sln -c Release --no-build
```

기대 결과: 모든 test가 통과하고 build warning은 0개여야 한다.

## 수동 테스트 Fixture

반복 가능한 수동 검증을 위해 bad-signal playback fixture를 포함했다.

```text
manual-fixtures/43200BPH_bad-signal_falseC_weak_192000Hz.wav
```

이 파일은 192 kHz / 43200 BPH watch-like signal이다. 43200 BPH는 현재 표준 BPH catalog의 최고값이며 beat period는 약 83.3 ms다. fixture는 high-rate 환경에서도 detector가 nominal beat rate를 유지하면서 signal-quality warning 경로를 확인할 수 있도록 weak-C / B-dominant / false-C-risk 조건을 포함한다.

파형 특징:

- A/B/C realistic packet을 사용한다. A cluster는 약간 낮추고, B cluster는 상대적으로 강하게, C cluster와 C anchor는 약하게 만들어 B 또는 noise peak가 C처럼 보일 수 있는 조건을 만든다.
- 192 kHz sample rate라 A-to-C marker와 C peak 주변의 미세 timing 차이를 더 촘촘한 sample 간격으로 볼 수 있다.
- 약한 C와 강한 B 때문에 일부 beat에서 `PossibleFalseC` 또는 `CTimingUnstable` warning이 발생할 수 있다.
- 낮은 band-limited noise와 드문 impulse noise를 포함하므로 clean synthetic signal이 아니라 handling/ambient-noise 위험을 흉내 내는 수동 검증용 입력이다.

Headless verifier는 nominal beat rate를 계속 감지해야 하고, GUI에서는 signal-quality warning 경로를 확인할 수 있어야 한다.

Verifier 기준 명령은 다음과 같다.

```powershell
dotnet run --project src/TimeGrapher.Verify -c Release -- manual-fixtures/43200BPH_bad-signal_falseC_weak_192000Hz.wav
```

생성된 fixture의 기대 기준 결과는 다음과 같다.

```text
detected_bph=43200
sync_status=Synced
results include Error Rate, Amplitude, Beat Error, and BPH 43200
```

## 수동 검증

### 1. Baseline Clean Signal

1. 앱을 Simulation mode에서 기본 설정 또는 clean setting으로 시작한다.
2. beat sync가 잡힐 때까지 기다린다.
3. Beat Noise, Waveform Compare, Escapement Analyzer를 연다.

기대 결과:

- 상단 measurement readout에 `Signal ...` suffix가 없어야 한다.
- Beat Noise에 quality overlay가 없어야 한다.
- Waveform Compare lane label에 `Signal: ...` 문구가 없어야 한다.
- Escapement Analyzer는 정상 repeatability verdict를 보여야 한다.

### 2. Possible False C / Unstable C

Playback mode에서 `manual-fixtures/43200BPH_bad-signal_falseC_weak_192000Hz.wav`를
사용한다. 또는 B/noise peak가 C로 선택되는 경우처럼, 특정 beat의 C marker가 최근
A-to-C pattern보다 비정상적으로 이르게 잡히는 synthetic capture를 사용한다.

기대 결과:

- 상단 measurement readout에 `Signal Possible false C` 또는 `Signal C timing unstable`이 표시된다.
- Beat Noise graph area에 `POSSIBLE FALSE C` 또는 `C TIMING UNSTABLE`이 표시된다.
- Waveform Compare 우측 상단 overlay에도 같은 warning이 표시되고, 영향받은 lane에는
  `Signal: Possible false C` label이 붙는다.
- Escapement Analyzer 우측 상단에도 같은 warning이 표시되며, 해당 beat를 정상
  repeatability sample처럼 처리하지 않는다.
- Status guidance는 Beat Noise를 확인하고 handling noise를 줄이라고 안내한다.
- Waveform Compare mean-C guide는 possible-false-C beat를 제외한다.
- 의심스러운 C는 amplitude reading을 갱신하지 않는다.

### 3. Overlay Fade-Out

1. bad-signal fixture 또는 동등한 입력으로 warning을 발생시킨다.
2. graph를 reset하지 않고 clean Simulation 또는 Playback signal로 전환한다.
3. Beat Noise, Waveform Compare, Escapement Analyzer를 관찰한다.

기대 결과:

- 마지막 warning은 clean signal update 10회 동안 완전히 보인다.
- 10번째 clean update 이후 warning이 점진적으로 희미해진다.
- 100번째 연속 clean update에서 warning이 사라진다.
- fade 중 새 warning이 발생하면 overlay는 새 warning text로 바뀌고 다시 완전히 보인다.

### 4. Weak Signal

A는 감지되지만 beat window 안에서 C marker를 사용할 수 없는 capture를 사용한다.

기대 결과:

- Beat Noise에 `WEAK SIGNAL`이 표시된다.
- 해당 quality가 shared snapshot에 도달하면 상단 readout에 `Signal Weak signal`이 표시된다.
- Status guidance는 watch 위치를 다시 잡거나 input gain을 높이라고 안내한다.

### 5. Runtime Quality와 Acoustic Signal Quality 구분

Acoustic signal 문제가 아니라 rendering deadline pressure 또는 analysis lag를 별도로 발생시킨다.

기대 결과:

- `Display quality was reduced to keep measurements responsive.`는 runtime/performance warning으로 유지된다.
- 이 문구는 acoustic signal-quality warning으로 처리하지 않는다.
- Signal-quality label은 beat/signal interpretation 문제에만 사용한다.

## 확인할 파일

- `src/TimeGrapher.Core/Shared/BeatSegmentsSnapshot.cs`
- `src/TimeGrapher.Core/Analysis/BeatSegmentCapture.cs`
- `src/TimeGrapher.Core/Metrics/WatchMetrics.cs`
- `src/TimeGrapher.App/Rendering/SignalQualityText.cs`
- `src/TimeGrapher.App/Rendering/SignalQualityOverlayState.cs`
- `src/TimeGrapher.App/Rendering/BeatNoiseScopeRenderer.cs`
- `src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs`
- `src/TimeGrapher.App/Rendering/WaveformCompareRenderer.cs`
- `src/TimeGrapher.App/Rendering/EscapementAnalyzerRenderer.cs`
- `src/TimeGrapher.App/Rendering/EscapementReadout.cs`
- `src/TimeGrapher.App/Services/AnalysisRunStatusReporter.cs`