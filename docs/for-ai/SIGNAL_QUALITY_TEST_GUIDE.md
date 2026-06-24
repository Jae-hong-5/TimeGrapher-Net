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
manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav
```

이 파일은 48 kHz / 21600 BPH watch-like signal이며 weak/false-C 조건을 포함한다.
Headless verifier는 nominal beat rate를 계속 감지해야 하고, GUI에서는 signal-quality
warning 경로를 확인할 수 있어야 한다.

Verifier 기준 명령은 다음과 같다.

```powershell
dotnet run --project src/TimeGrapher.Verify -c Release -- manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav
```

생성된 fixture의 기대 기준 결과는 다음과 같다.

```text
detected_bph=21600
sync_status=Synced
results include Error Rate, Amplitude, Beat Error, and BPH 21600
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

Playback mode에서 `manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav`를
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