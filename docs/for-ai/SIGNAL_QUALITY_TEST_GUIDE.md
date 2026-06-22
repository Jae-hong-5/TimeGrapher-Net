# Signal Quality Test Guide

This guide covers the stage 1 and stage 2 signal-quality changes on
`feature/signal-quality-propagation`.

## What Changed

The app now carries signal-quality warnings through the shared beat-segment DTOs.
The current flags are:

- `WeakSignal`: no usable C marker was found for the displayed beat segment.
- `NoisySignal`: the detected C timing is inconsistent with recent A-to-C timing.
- `CTimingUnstable`: the A-to-C interval deviates from the recent median/MAD band.
- `PossibleFalseC`: the C candidate is unusually early and may be B/noise.
- `ClippedSignal`: reserved for clipping classification.
- `NoSignal`: reserved for no-signal classification.

Stage 1 made the shared quality state visible in the top readout and Beat Noise.
Stage 2 adds recovery guidance, excludes suspicious C candidates from amplitude
updates, and propagates quality into beat-aligned analysis views.

## Automated Verification

Run the full build:

```powershell
dotnet build TimeGrapherNet.sln -c Release
```

Run focused Core tests:

```powershell
dotnet test tests/TimeGrapher.Core.Tests/TimeGrapher.Core.Tests.csproj -c Release --no-build --filter "BeatSegmentCaptureTests|WatchMetricsTests"
```

Run focused App tests:

```powershell
dotnet test tests/TimeGrapher.App.Tests/TimeGrapher.App.Tests.csproj -c Release --no-build --filter "WaveformCompareLogicTests|AnalysisRunStatusReporterTests|SignalQualityTextTests"
```

Run the full test suite:

```powershell
dotnet test TimeGrapherNet.sln -c Release --no-build
```

Expected result: all tests pass with zero build warnings.

## Manual Verification

### 1. Baseline Clean Signal

1. Start the app in Simulation mode with default or clean settings.
2. Wait for beat sync.
3. Open Beat Noise, Waveform Compare, and Escapement Analyzer.

Expected result:

- The top measurement readout has no `Signal ...` suffix.
- Beat Noise shows no quality overlay.
- Waveform Compare lane labels do not include `Signal: ...`.
- Escapement Analyzer shows the normal repeatability verdict.

### 2. Possible False C / Unstable C

Use a playback fixture or synthetic capture where one beat has an early C marker
relative to the recent A-to-C pattern, such as a B/noise peak being selected as C.

Expected result:

- The top measurement readout shows `Signal Possible false C` or
  `Signal C timing unstable`.
- Beat Noise displays `POSSIBLE FALSE C` or `C TIMING UNSTABLE`.
- Status guidance says to check Beat Noise and reduce handling noise.
- Waveform Compare labels the affected lane with `Signal: Possible false C`.
- Waveform Compare mean-C guide ignores the possible-false-C beat.
- Escapement Analyzer reports the signal warning instead of treating the beat as
  a normal repeatability sample.
- The suspicious C does not update the amplitude reading.

### 3. Weak Signal

Use a capture where A is detected but no C marker is available in the beat window.

Expected result:

- Beat Noise displays `WEAK SIGNAL`.
- The top readout shows `Signal Weak signal` when that quality reaches the shared
  snapshot.
- Status guidance recommends repositioning the watch or increasing input gain.

### 4. Runtime Quality Is Separate

Trigger rendering deadline pressure or analysis lag separately from acoustic
signal issues.

Expected result:

- `Display quality was reduced to keep measurements responsive.` remains a
  runtime/performance warning, not an acoustic signal-quality warning.
- Signal-quality labels are only used for beat/signal interpretation issues.

## Files To Inspect

- `src/TimeGrapher.Core/Shared/BeatSegmentsSnapshot.cs`
- `src/TimeGrapher.Core/Analysis/BeatSegmentCapture.cs`
- `src/TimeGrapher.Core/Metrics/WatchMetrics.cs`
- `src/TimeGrapher.App/Rendering/SignalQualityText.cs`
- `src/TimeGrapher.App/Rendering/BeatNoiseScopeRenderer.cs`
- `src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs`
- `src/TimeGrapher.App/Rendering/EscapementReadout.cs`
- `src/TimeGrapher.App/Services/AnalysisRunStatusReporter.cs`
