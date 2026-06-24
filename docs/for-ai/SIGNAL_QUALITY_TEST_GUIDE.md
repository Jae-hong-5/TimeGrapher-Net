# Signal Quality Test Guide

This guide covers the stage 1, stage 2, and graph-overlay signal-quality changes on
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
updates, and propagates quality into beat-aligned analysis views. The graph-overlay
pass adds a top-right warning overlay to Beat Noise, Waveform Compare, and
Escapement Analyzer so the warning is visible in the diagnostic view the user is
currently inspecting.

## Overlay Fade Contract

When a graph receives a non-clean `SignalQualityFlags` value, the overlay shows the
latest warning at full opacity. When subsequent frames are clean, the overlay keeps
the last warning visible for 10 consecutive clean updates, then fades linearly until
it disappears on the 100th clean update.

This is intentionally frame-count based, not wall-clock based, so Playback, Live,
and Simulation modes behave consistently under the same analysis-frame sequence.

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

Run the combined focused suite used for this branch:

```powershell
dotnet test TimeGrapherNet.sln -c Release --no-build --filter "SignalQualityTextTests|AnalysisRunStatusReporterTests|WaveformCompareLogicTests|BeatSegmentCaptureTests|WatchMetricsTests|BeatNoiseScopeRendererTests|Escapement"
```

Run the full test suite:

```powershell
dotnet test TimeGrapherNet.sln -c Release --no-build
```

Expected result: all tests pass with zero build warnings.

## Manual Fixture

A bad-signal playback fixture is included for repeatable manual checks:

```text
manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav
```

This file is a 48 kHz / 21600 BPH watch-like signal with weak/false-C conditions.
The headless verifier should still detect the nominal beat rate while surfacing the
signal-quality warning path in the GUI.

Reference verify command:

```powershell
dotnet run --project src/TimeGrapher.Verify -c Release -- manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav
```

Expected reference result from the generated fixture:

```text
detected_bph=21600
sync_status=Synced
results include Error Rate, Amplitude, Beat Error, and BPH 21600
```

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

Use `manual-fixtures/21600BPH_bad-signal_falseC_weak_48000Hz.wav` in Playback mode,
or use a synthetic capture where one beat has an early C marker relative to the
recent A-to-C pattern, such as a B/noise peak being selected as C.

Expected result:

- The top measurement readout shows `Signal Possible false C` or
  `Signal C timing unstable`.
- Beat Noise displays `POSSIBLE FALSE C` or `C TIMING UNSTABLE` in the graph area.
- Waveform Compare displays the same warning as a top-right overlay and labels the
  affected lane with `Signal: Possible false C`.
- Escapement Analyzer displays the same top-right warning instead of treating the
  beat as a normal repeatability sample.
- Status guidance says to check Beat Noise and reduce handling noise.
- Waveform Compare mean-C guide ignores the possible-false-C beat.
- The suspicious C does not update the amplitude reading.

### 3. Overlay Fade-Out

1. Trigger a warning with the bad-signal fixture or equivalent input.
2. Switch back to a clean Simulation or Playback signal without resetting the graph.
3. Watch Beat Noise, Waveform Compare, and Escapement Analyzer.

Expected result:

- The last warning remains fully visible for 10 consecutive clean signal updates.
- The warning fades gradually after the 10th clean update.
- The warning disappears on the 100th consecutive clean update.
- A new warning during the fade returns the overlay to full opacity with the new
  latest warning text.

### 4. Weak Signal

Use a capture where A is detected but no C marker is available in the beat window.

Expected result:

- Beat Noise displays `WEAK SIGNAL`.
- The top readout shows `Signal Weak signal` when that quality reaches the shared
  snapshot.
- Status guidance recommends repositioning the watch or increasing input gain.

### 5. Runtime Quality Is Separate

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
- `src/TimeGrapher.App/Rendering/SignalQualityOverlayState.cs`
- `src/TimeGrapher.App/Rendering/BeatNoiseScopeRenderer.cs`
- `src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs`
- `src/TimeGrapher.App/Rendering/WaveformCompareRenderer.cs`
- `src/TimeGrapher.App/Rendering/EscapementAnalyzerRenderer.cs`
- `src/TimeGrapher.App/Rendering/EscapementReadout.cs`
- `src/TimeGrapher.App/Services/AnalysisRunStatusReporter.cs`