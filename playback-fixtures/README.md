# Playback Fixture Guide

This directory contains deterministic WAV inputs for exercising TimeGrapher playback and detection behavior. The numbered fixtures (`1_` through `6_`) are the primary automation set. Other WAV files in this directory are legacy/support fixtures and are documented separately below.

All primary fixtures are mono 32-bit float WAV files accepted by the app playback path. They are intended to be used through either the GUI Playback mode or the headless verification commands shown here.

## Automation Commands

Run detector/metrics verification for only the primary six fixtures:

```powershell
dotnet run --project src\TimeGrapher.Verify\TimeGrapher.Verify.csproj -c Release -- `
  playback-fixtures\1_18000BPH_clean-18000_48000Hz.wav `
  playback-fixtures\2_21600BPH_realistic-21600_48000Hz.wav `
  playback-fixtures\3_28800BPH_noisy-28800_48000Hz.wav `
  playback-fixtures\4_36000BPH_highrate-36000_96000Hz.wav `
  playback-fixtures\5_21600BPH_fast-plus30s_48000Hz.wav `
  playback-fixtures\6_21600BPH_beaterror5ms_48000Hz.wav
```

Run the app analysis path, which reads the WAV as playback input and feeds `AnalysisWorker` without opening the GUI:

```powershell
foreach ($f in @(
  'playback-fixtures\1_18000BPH_clean-18000_48000Hz.wav',
  'playback-fixtures\2_21600BPH_realistic-21600_48000Hz.wav',
  'playback-fixtures\3_28800BPH_noisy-28800_48000Hz.wav',
  'playback-fixtures\4_36000BPH_highrate-36000_96000Hz.wav',
  'playback-fixtures\5_21600BPH_fast-plus30s_48000Hz.wav',
  'playback-fixtures\6_21600BPH_beaterror5ms_48000Hz.wav'
)) {
  dotnet run --project src\TimeGrapher.App\TimeGrapher.App.csproj -c Release -- --analysis-benchmark --wav $f
}
```

For CI-style assertions, parse these fields from `TimeGrapher.Verify` output:

- `detected_bph` must match the BPH embedded in the file name.
- `sync_status` must be `Synced` for fixtures 1-6.
- `results` must be non-empty.
- Fixture 5 should report an obviously positive rate near `+30 s/d`.
- Fixture 6 should report beat error near `5.0 ms` and should trigger G06 separation alert behavior in the GUI.

## Primary Fixtures

| ID | File | Signal profile | Expected verification behavior | GUI/playback behavior to observe |
|---|---|---|---|---|
| 1 | `1_18000BPH_clean-18000_48000Hz.wav` | Clean synthetic watch at 18,000 BPH, 48 kHz, no added noise, nominal rate, nominal beat error. | `detected_bph=18000`, `sync_status=Synced`, rate around `+0.0 s/d`, amplitude around `273°`, beat error around `0.0 ms`. | Baseline control case. Rate/Scope and G06 should show stable, nearly horizontal tic/toc traces with very small separation. G05 should show clear beat-noise waveform and stable A/C markers. |
| 2 | `2_21600BPH_realistic-21600_48000Hz.wav` | Realistic synthetic 21,600 BPH signal at 48 kHz with moderate natural variation/noise model. | `detected_bph=21600`, `sync_status=Synced`, rate currently observed around `+2.3 s/d`, amplitude around `275°`, beat error around `0.1 ms`. | Normal non-ideal playback case. GUI should acquire sync and continue updating without warnings for major G06 faults. G05 waveform should look less ideal than fixture 1 but still readable. |
| 3 | `3_28800BPH_noisy-28800_48000Hz.wav` | 28,800 BPH signal at 48 kHz with added broadband noise. | `detected_bph=28800`, `sync_status=Synced`, rate around `+0.1 s/d`, amplitude around `273°`, beat error around `0.0 ms`. | Noise-tolerance case. Detection should still sync. G05 is useful here: waveform should show visible noise, while averaged/strip views should remain usable. G06 should not show large beat-error separation. |
| 4 | `4_36000BPH_highrate-36000_96000Hz.wav` | High beat-rate 36,000 BPH signal at 96 kHz. Shorter beat period and higher sample rate. | `detected_bph=36000`, `sync_status=Synced`, rate around `0.0 s/d`, amplitude around `273°`, beat error around `0.0 ms`. | High-rate and 96 kHz playback case. The app should handle the denser input without deadline degradation. G06 X-axis will fill faster because beats arrive more frequently. |
| 5 | `5_21600BPH_fast-plus30s_48000Hz.wav` | 21,600 BPH signal at 48 kHz with synthetic rate error of about `+30 s/d`. | `detected_bph=21600`, `sync_status=Synced`, rate currently observed around `+29.9 s/d`, amplitude around `273°`, beat error around `0.0 ms`. | G06 slope/fast-watch case. The rate trace should trend upward relative to nominal timing. This is useful for checking that positive rate readings correspond to positive trace slope. It is not intended to trigger beat-error separation. |
| 6 | `6_21600BPH_beaterror5ms_48000Hz.wav` | 21,600 BPH signal at 48 kHz with large synthetic beat error of `5 ms`. | `detected_bph=21600`, `sync_status=Synced`, rate around `+0.0 s/d`, amplitude around `273°`, beat error around `5.0 ms`. | G06 separation-alert case. Tic/toc traces should be visibly separated, and the Beat Error tab should show the separation alert because the current alert threshold is `0.6 ms`. This is the best fixture for testing the larger G06 point markers and adaptive X-axis visibility. |

## Expected Output Snapshot

Current verification output for fixtures 1-6:

```text
1_18000BPH_clean-18000_48000Hz.wav: detected_bph=18000 sync_status=Synced results=[RATE   +0.0 s/d | AMPLITUDE 273° | BEAT ERROR  0.0 ms | BEAT 18000 bph]
2_21600BPH_realistic-21600_48000Hz.wav: detected_bph=21600 sync_status=Synced results=[RATE   +2.3 s/d | AMPLITUDE 275° | BEAT ERROR  0.1 ms | BEAT 21600 bph]
3_28800BPH_noisy-28800_48000Hz.wav: detected_bph=28800 sync_status=Synced results=[RATE   +0.1 s/d | AMPLITUDE 273° | BEAT ERROR  0.0 ms | BEAT 28800 bph]
4_36000BPH_highrate-36000_96000Hz.wav: detected_bph=36000 sync_status=Synced results=[RATE   -0.0 s/d | AMPLITUDE 273° | BEAT ERROR  0.0 ms | BEAT 36000 bph]
5_21600BPH_fast-plus30s_48000Hz.wav: detected_bph=21600 sync_status=Synced results=[RATE  +29.9 s/d | AMPLITUDE 273° | BEAT ERROR  0.0 ms | BEAT 21600 bph]
6_21600BPH_beaterror5ms_48000Hz.wav: detected_bph=21600 sync_status=Synced results=[RATE   +0.0 s/d | AMPLITUDE 273° | BEAT ERROR  5.0 ms | BEAT 21600 bph]
```

Do not assert exact string spacing in automation. Parse numeric fields or use tolerant regular expressions.

## Suggested Test Assertions

Use tolerant thresholds because detector formatting and small timing differences may shift slightly after algorithm changes.

| Fixture | Required sync | Expected BPH | Suggested rate assertion | Suggested amplitude assertion | Suggested beat-error assertion |
|---|---:|---:|---:|---:|---:|
| 1 | `Synced` | 18000 | `abs(rate) <= 1.0 s/d` | `250° <= amplitude <= 300°` | `abs(beatError) <= 0.2 ms` |
| 2 | `Synced` | 21600 | `abs(rate) <= 5.0 s/d` | `250° <= amplitude <= 300°` | `abs(beatError) <= 0.5 ms` |
| 3 | `Synced` | 28800 | `abs(rate) <= 2.0 s/d` | `250° <= amplitude <= 300°` | `abs(beatError) <= 0.5 ms` |
| 4 | `Synced` | 36000 | `abs(rate) <= 1.0 s/d` | `250° <= amplitude <= 300°` | `abs(beatError) <= 0.2 ms` |
| 5 | `Synced` | 21600 | `25.0 <= rate <= 35.0 s/d` | `250° <= amplitude <= 300°` | `abs(beatError) <= 0.2 ms` |
| 6 | `Synced` | 21600 | `abs(rate) <= 1.0 s/d` | `250° <= amplitude <= 300°` | `4.5 <= beatError <= 5.5 ms` |

For G06 GUI-specific automation, add these higher-level assertions when UI automation is available:

- Fixture 1: no separation alert, no major fault banner.
- Fixture 5: trace slope should be visibly positive; no separation alert expected.
- Fixture 6: separation alert banner should be visible; major fault is not the primary expected condition.

## App Analysis Benchmark Expectations

The app benchmark path should report the expected BPH for all primary fixtures and keep deadline degradation at zero on a normal development machine.

Recent benchmark observations:

| Fixture | Expected BPH | Frames | Detected BPH | Max deadline level |
|---|---:|---:|---:|---:|
| 1 | 18000 | 142 | 18000 | 0 |
| 2 | 21600 | 142 | 21600 | 0 |
| 3 | 28800 | 142 | 28800 | 0 |
| 4 | 36000 | 236 | 36000 | 0 |
| 5 | 21600 | 142 | 21600 | 0 |
| 6 | 21600 | 142 | 21600 | 0 |

For automation, avoid gating on exact processing time because it depends on the machine. Prefer:

- command exit code is `0`;
- `detected_bph` equals expected BPH;
- `frames > 0`;
- `max_deadline_level == 0` for local performance sanity checks only.

## Weak Signal Simulation Fixtures

아래 3개 파일은 `WEAK SIGNAL` 표시를 확인하기 위해 생성한 21,600 BPH 시뮬레이션 파형이다. 모두 mono 32-bit float WAV이며 앱의 `Playback` 입력과 `--analysis-benchmark --wav` 경로에서 읽을 수 있다.

| File | Waveform profile | Expected GUI behavior | Headless assertion |
|---|---|---|---|
| `21600BPH_normal_aonly_noise_normal_48k_float.wav` | 0-4초 정상 A/C, 4-7초 약한 A-like pulse + 백색잡음, 7-12초 정상 A/C. | 4-7초 구간에서 Beat Noise 상단 그래프에 `WEAK SIGNAL`이 보여야 한다. | `detected_bph=21600`, `frames > 0`, `max_deadline_level=0`. |
| `21600BPH_normal_closecweak_normal_48k_float.wav` | 0-4초 정상 A/C, 4-7초 A 바로 뒤에 붙은 close-C/꼬리성 C + 잡음, 7-12초 정상 A/C. | A 바로 뒤 가짜 C를 정상 A/C로 보지 않아야 하며, 4-7초 구간에서 `WEAK SIGNAL`이 보여야 한다. | `detected_bph=21600`, `frames > 0`, `max_deadline_level=0`. |
| `21600BPH_normal_noisegap_normal_48k_float.wav` | 0-4초 정상 A/C, 4-7초 백색잡음 only, 7-12초 정상 A/C. | detector가 새 segment를 만들지 않거나 낮은/불완전 segment를 만들 수 있다. GUI에서는 정상 A/C로 신뢰하지 않는 구간을 수동 확인하는 보조 fixture다. | `detected_bph=21600`, `frames > 0`, `max_deadline_level=0`. |

### Weak Signal Test Commands

Headless benchmark는 WAV가 앱 playback 분석 경로로 정상 처리되는지 확인한다. `WEAK SIGNAL` 자체는 GUI plottable 상태이므로 App renderer unit test와 GUI Playback 수동 확인을 함께 사용한다.

```powershell
# Build and renderer tests, including weak-signal plottable assertions
dotnet build TimeGrapherNet.sln -c Release
dotnet test tests\TimeGrapher.App.Tests\TimeGrapher.App.Tests.csproj -c Release
dotnet run --project src\TimeGrapher.App -c Release -- --smoke

# App analysis path for each weak-signal WAV
dotnet run --project src\TimeGrapher.App -c Release -- --analysis-benchmark --wav "playback-fixtures\21600BPH_normal_aonly_noise_normal_48k_float.wav" --bph 21600 --duration-ms 12000
dotnet run --project src\TimeGrapher.App -c Release -- --analysis-benchmark --wav "playback-fixtures\21600BPH_normal_closecweak_normal_48k_float.wav" --bph 21600 --duration-ms 12000
dotnet run --project src\TimeGrapher.App -c Release -- --analysis-benchmark --wav "playback-fixtures\21600BPH_normal_noisegap_normal_48k_float.wav" --bph 21600 --duration-ms 12000
```

GUI 수동 확인 절차:

1. `D:\swa\TimeGrapher-Net\publish\win-x64\TimeGrapher.App.exe` 또는 `dotnet run --project src\TimeGrapher.App -c Release`로 앱을 실행한다.
2. 입력 source를 `Playback`으로 선택한다.
3. 위 weak-signal WAV 중 하나를 선택한다.
4. Beat Noise 탭에서 상단 Beat Scope를 본다.
5. 0-4초와 7-12초 정상 구간에서는 A/C marker가 분리되어 보이고 `WEAK SIGNAL`이 없어야 한다.
6. 4-7초 약한/불완전 구간에서는 상단 그래프 우측 상단에 `WEAK SIGNAL`이 보여야 한다.

### Current Weak Signal Results

Latest local run on 2026-06-15:

```text
dotnet build TimeGrapherNet.sln -c Release
=> build succeeded, warnings=0, errors=0

dotnet test tests\TimeGrapher.App.Tests\TimeGrapher.App.Tests.csproj -c Release
=> passed: 311, failed: 0, skipped: 0

dotnet run --project src\TimeGrapher.App -c Release -- --smoke
=> TimeGrapher.App smoke OK

analysis_benchmark source=21600BPH_normal_aonly_noise_normal_48k_float.wav expected_bph=21600 sample_rate=48000 duration_ms=12000 frames=142 detected_bph=21600
budget beat_period_ms=166.667 processing_to_audio_ratio=4.70 % max_lag_ms=0.000 max_deadline_level=0

analysis_benchmark source=21600BPH_normal_closecweak_normal_48k_float.wav expected_bph=21600 sample_rate=48000 duration_ms=12000 frames=142 detected_bph=21600
budget beat_period_ms=166.667 processing_to_audio_ratio=4.05 % max_lag_ms=0.000 max_deadline_level=0

analysis_benchmark source=21600BPH_normal_noisegap_normal_48k_float.wav expected_bph=21600 sample_rate=48000 duration_ms=12000 frames=142 detected_bph=21600
budget beat_period_ms=166.667 processing_to_audio_ratio=3.90 % max_lag_ms=0.000 max_deadline_level=0
```

Automation note: headless benchmark validates ingestion, sync, and deadline behavior only. The actual `WEAK SIGNAL` text is covered by `BeatNoiseScopeRendererTests` (`MainScopeShowsWeakSignalWhenCMarkerIsMissing`, `MainScopeShowsWeakSignalWhenCMarkerIsTooCloseToA`, `MainScopeShowsWeakSignalWhenPeakIsTooLow`) and by GUI Playback observation.

## Additional Support Fixture Summary

The weak-signal WAV files above are support fixtures, not part of the primary 1-6 happy-path automation set. Keep them for edge coverage and include them only in tests that explicitly expect weak/ambiguous Beat Noise behavior.

| File | Current observed behavior | Suggested use |
|---|---|---|
| `21600BPH_normal_aonly_noise_normal_48k_float.wav` | `detected_bph=21600`, `sync_status=Synced`, rate around `+0.6 s/d`, amplitude around `153°`, beat error around `0.0 ms`. | Low-amplitude / A-dominant edge case. Good for checking that sync can still be acquired while amplitude may be outside healthy range. |
| `21600BPH_normal_closecweak_normal_48k_float.wav` | `detected_bph=21600`, `sync_status=Synced`, rate around `+0.7 s/d`, amplitude around `319°`, beat error around `0.0 ms`. | Weak/close C-event edge case. Good for amplitude and C-event robustness checks. |
| `21600BPH_normal_noisegap_normal_48k_float.wav` | `detected_bph=21600`, `sync_status=Synced`, rate around `+0.7 s/d`, amplitude missing (`---°`), beat error around `0.6 ms`. | Missing/ambiguous amplitude edge case. Good for graceful degradation checks where BPH sync works but amplitude should not be trusted. |

## Manual Playback Procedure

To test through the GUI:

1. Launch `TimeGrapher.App.exe`.
2. Select input source `Playback`.
3. Start playback and choose one WAV from this directory.
4. Confirm the measurement summary bar reaches the expected BPH and values above.
5. For G06, use fixtures 5 and 6:
   - fixture 5 should show a positive rate trend;
   - fixture 6 should show visible tic/toc separation and a separation alert.
6. For G05, use fixtures 2 and 3:
   - fixture 2 should show realistic but readable beat shape;
   - fixture 3 should show noise while still maintaining sync.

## Notes for Future Fixture Changes

- Keep BPH in the filename as `<number>BPH`; `TimeGrapher.Verify` parses this for expected BPH.
- Prefer 48 kHz for ordinary fixtures and 96 kHz only when testing higher sample-rate behavior.
- If a fixture intentionally should not sync, document it separately and do not include it in the primary 1-6 happy-path set.
- If algorithm thresholds change, update the observed output snapshot and suggested tolerance table together.
