# SAP Tactics Analysis

> CMU-LG Software Architecture Training Course artifact.
> Basis: Bass, Clements, Kazman, *Software Architecture in Practice* (SAP).

This document records the architectural tactics and patterns that are central to
TimeGrapherNet. It is intentionally not a full implementation log. Detailed data
contracts and dependency graphs live in `DATA_MODEL_VIEW.md` and
`MODULE_USES_VIEW.md`.

Applicability marks:

| Mark | Meaning |
|---|---|
| ✓ | Direct SAP tactic or pattern application |
| △ | Similar, but only partially matches the textbook definition |
| ✗ | Considered but not actually applicable |

## 1. Architecture Drivers

TimeGrapherNet receives watch audio, detects beat events, derives watch metrics,
and renders them live. The architecture is driven by five concerns.

| Driver | Architectural pressure |
|---|---|
| Performance | 28800 BPH produces one beat every 125 ms. Analysis, history, and UI rendering must not accumulate unbounded work. |
| Modifiability | Detector, metrics, UI, and OS audio code must remain separable so course changes can be traced and reviewed. |
| Availability | Live input can disappear, stop can be delayed, and old worker callbacks can arrive after a new run starts. |
| Portability | One Avalonia app must run on Windows and Linux/Raspberry Pi through platform adapters. |
| Testability | Detector and UI behavior must be reproducible without a microphone or GUI. |

## 2. Architectural Baseline

The main dependency rule is:

```text
TimeGrapher.App -> TimeGrapher.Core / TimeGrapher.Inference / TimeGrapher.Platform.*
TimeGrapher.Platform.* -> TimeGrapher.Core
TimeGrapher.Inference -> TimeGrapher.Core
TimeGrapher.Verify -> TimeGrapher.Core
TimeGrapher.Core -> no project, UI, platform, or package dependency
```

Runtime data follows this path:

```text
Audio worker -> MasterAudioBuffer -> AnalysisWorker
AnalysisWorker -> AnalysisFrame -> AnalysisFrameRenderScheduler
AnalysisFrameRenderScheduler -> active tab renderer / global UI state
```

This baseline is the source of most tactics below.

## 3. Key Tactics by Quality Attribute

### 3.1 Modifiability

| SAP tactic | How the project applies it | Evidence | Mark |
|---|---|---|---|
| Restrict dependencies | `Core` owns detection, metrics, shared DTOs, imaging, simulation, and audio file logic without UI or platform references. App and platform projects depend inward. CI checks protect this direction. | `TimeGrapher.Core.csproj`, `.github/workflows/ci.yml`, `MODULE_USES_VIEW.md` | ✓ |
| Encapsulate | Windows NAudio and Linux PipeWire/ALSA details are hidden behind live-audio worker contracts. App code starts a worker; it does not consume OS audio APIs directly. | `IAudioInputWorker.cs`, `ILiveAudioWorker.cs`, `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs` | ✓ |
| Use an intermediary | `LiveAudioBackend` is the platform selection boundary. It is the narrow place where RID/platform conditions become concrete worker creation. | `LiveAudioBackend.cs`, `TimeGrapher.App.csproj` | ✓ |
| Abstract common services | Shared settings and policy surfaces keep multiple displays consistent instead of duplicating thresholds. Accept bands are read through `AcceptBandSettings.Current`; sampling defaults are read through `SamplingSettings`. | `AcceptBandSettings.cs`, `LongTermAcceptPolicy.cs`, `TraceAlertEvaluator.cs`, `SamplingSettings.cs` | ✓ |
| Single verdict source (another consumer over one snapshot) | Cross-position consistency (OK/CHECK/COLLECTING plus the D-spread / balance-wheel / V·H requirements) is computed once in the pure `ConsistencyDiagnosis` from the per-position snapshot, then consumed by the Health diagnosis rail alongside the per-position band-conformance levels. The Positions tab re-renders the same snapshot as a pure data view (means table + `RateRangeLaneControl` acquisition lanes) and no longer judges consistency, so the rule lives in one place and both tabs read the same accept bands. No new analysis data is added. | `ConsistencyDiagnosis.cs`, `SequenceSummary.cs`, `WatchHealthRadarModel.cs`, `RateRangeLaneControl.cs` | ✓ |
| Split module | Run lifecycle, selection coordination, dialogs, measurement logging, AI backend calls, credential persistence, and rendering are kept in focused services/renderers while preserving the Avalonia MVVM boundary. | `RunCommandService.cs`, `RunSessionController.cs`, `AiAnalysisController.cs`, `AiAnalysisService.cs`, `AiCredentialStore.cs`, `InfoTabRegistry.cs` | ✓ |

### 3.2 Performance

| SAP tactic | How the project applies it | Evidence | Mark |
|---|---|---|---|
| Introduce concurrency | Input, analysis, recording, and UI rendering are separated. The UI receives finished `AnalysisFrame`s instead of running detector work. | `AnalysisWorker.cs`, `SimWorker.cs`, `PlaybackWorker.cs`, `QueuedWavStreamWriter.cs` | ✓ |
| Limit event response | The render scheduler keeps the latest frame and coalesces intermediate frames. One-shot signals such as input overrun are merged so dropping intermediate UI frames does not drop important state. | `AnalysisFrameRenderScheduler.cs` | ✓ |
| Schedule resources | Most frames render only the active tab. All tabs may observe lightweight state, but heavy graph rendering is tab-selected. | `AnalysisFrameRouter.cs`, `IAnalysisFrameConsumer.cs` | ✓ |
| Bound queue sizes | Audio history, render handoff, recording queues, AI log upload input, backend response reading, and AI Markdown display work are bounded. Overflow is explicit rather than silently growing memory or blocking analysis/UI indefinitely. | `MasterAudioBuffer.cs`, `QueuedWavStreamWriter.cs`, `AnalysisFrameRenderScheduler.cs`, `AiAnalysisController.cs`, `AiAnalysisService.cs`, `MarkdownDisplayRenderer.cs` | ✓ |
| Reduce overhead | Rate, period, and statistics use incremental rolling calculations. Graphs use point budgets and decimated history rather than redrawing unbounded raw samples. | `RollingAverage.cs`, `RollingLeastSquares.cs`, `SeriesDataReducer.cs`, `DecimatingSeries.cs`, `InfoTabCatalog.cs` | ✓ |
| Bound resource usage | Long-running metric history is stored as bounded decimating series. Sound/beat image buffers use fixed pools or snapshots so runtime cost does not grow linearly with session length. | `BeatMetricsHistory.cs`, `SoundPrintFrameProjector.cs`, `SpectrogramFrameProjector.cs`, `BeatSegmentCapture.cs` | ✓ |
| Maintain multiple copies of data | Sound/spectrogram publish pools and the beat-segment ring rotate protected snapshots: a published buffer is overwritten again only after enough newer publishes, and latest-wins delivery keeps the UI within one publish of the newest image, so on-screen reads never touch a buffer being recycled by analysis. | `SoundPrintFrameProjector.cs`, `SpectrogramFrameProjector.cs`, `BeatSegmentCapture.cs` | ✓ |
| Monitor and degrade | The degradation ladder is triggered by analysis backlog only: `AnalysisDeadlineMonitor` acts solely on analysis lag, and sustained lag above the breach threshold reduces visual work to keep the backlog from growing further — reactive throttling, not preemptive. Processing and display latency are measured for telemetry/status (the processing-time per frame and the end-to-end display latency feed logging and the status text) but do not themselves drive the degradation ladder. | `AnalysisDeadlineMonitor.cs`, `AnalysisWorker.cs`, `LatencyStatsTracker.cs`, `AnalysisPerformanceLogger.cs` | ✓ |

### 3.3 Availability and Reliability

| SAP tactic | How the project applies it | Evidence | Mark |
|---|---|---|---|
| Timestamp | Three coordinated monotonic counters guard run identity: a run-session token (input-worker callbacks), an analysis-session id (analysis frames), and a render generation. Late callbacks from an old run are ignored before they can mutate the new run. | `RunSessionController.cs`, `AnalysisFrameRenderScheduler.cs` | ✓ |
| Fault detection | Workers report completion/failure, live capture distinguishes requested stop from unexpected end, and detector/metric outputs carry sync and quality payloads (the App maps those Core payloads to user-facing text). | `PlaybackWorker.cs`, `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs`, `DetectorMetricsEngine.cs`, `BeatSegmentsSnapshot.cs`, `BeatSegmentCapture.cs` | ✓ |
| Fault recovery | Run control is modeled as explicit states: stopped, starting, running, paused, stopping, and stop-failed. A failed stop keeps retry/reset paths available instead of leaving the UI in an unknown state; natural playback/simulation EOF first tries to drain the final analysis frame, then falls back to an interrupting stop if the drain exceeds the normal stop budget and warns that measurement evidence may be incomplete. | `RunCommandService.cs`, `RunCommandService.States.cs`, `MainWindow.RunLifecycle.cs`, `RunSessionController.cs`, `AnalysisWorker.cs`, `RunCommandServiceTests.cs` | ✓ |
| Sanity checking | Live device/sample-rate choices are probed before use. On Linux/Raspberry Pi, PipeWire capture sources are matched back to their ALSA hardware when possible, first through `wpctl inspect` ALSA card/device metadata and then by device-name fallback, so the sample-rate menu is narrowed by the hardware `arecord` probe instead of accepting every rate that PipeWire can resample. Live-device cache misses keep the rate list empty until the probe returns, rather than briefly showing unverified rates. Settings loaded from JSON are validated before replacing current defaults. AI backend base URLs are constrained to the approved HTTPS allowlist, logs are checked against the 90,000-character client limit before upload, and credential persistence is enabled only after an OS credential-store store-and-read round-trip probe succeeds (the probe also issues a delete/clear, but that cleanup is best-effort and its result is not asserted). | `AudioDeviceController.cs`, `AudioCaptureWorker.cs`, `LinuxLiveAudioWorker.cs`, `AppSettingsStore.cs`, `AiAnalysisController.cs`, `AiAnalysisService.cs`, `AiCredentialStore.cs` | ✓ |
| Ignore faulty input and resynchronize | Detection gaps reset affected metric accumulation, prevent stale beat-error/rate values from leaking across lock changes, and count missed beats without interpolating false data. | `WatchMetrics.cs`, `BeatMetricsHistory.cs` | ✓ |
| Condition monitoring | Detector thresholds adapt to weak/noisy signal conditions and detect regime changes such as sudden gain shifts. This is now normal detector behavior rather than an optional UI feature. | `Detector.cs`, `AdaptiveFloorTests.cs`, `RegimeGuardTests.cs`, `AdverseScenarios.cs` | ✓ |
| Ignore faulty behavior (acquisition) | A weak between-beat artifact near the half-beat aliases the detected BPH to 2x during acquisition (it folds in-phase at the half period and drops the median A-to-A interval below the plausibility floor). The acquisition spurious-beat gate rejects, while unsynced, a burst far weaker than the recent accepted beats so the true cadence locks. Exposed as the "Enhanced Auto BPH" toggle (on by default in the app; off in the library, so default detection stays bit-identical). Never runs post-lock, where the phase guide already owns onset gating. The clean-signal alias margin is thin -- the 2x candidate scores ~0.98 even on an artifact-free 21600 stream, just under the true cadence -- so a small half-beat contamination is enough to flip the pick, which is why the gate is needed; a future scoring change that narrows this near-tie would make the alias more likely and is worth re-reviewing against this tactic. | `Detector.cs`, `TgDetector.cs`, `AcquisitionPeakGateTests.cs` | ✓ |

### 3.4 Testability

| SAP tactic | How the project applies it | Evidence | Mark |
|---|---|---|---|
| Limit nondeterminism | Synthetic watch audio is generated from deterministic seeded configuration, so detector changes can be compared reproducibly. | `WatchSynthStream.cs`, `WatchSynthStreamTests.cs` | ✓ |
| Abstract data sources | Live, playback, and synthetic input all feed the same analysis contracts, allowing file/synthetic tests to exercise the same detector and metric path. | `IAudioInputWorker.cs`, `DetectorMetricsEngine.cs`, `AnalysisWorker.cs` | ✓ |
| Specialized interfaces | `TimeGrapher.Verify` and app smoke/benchmark modes provide headless verification surfaces outside the GUI. | `src/TimeGrapher.Verify/Program.cs`, `AudioSmokeRunner.cs`, `AnalysisBenchmarkRunner.cs` | ✓ |
| Executable assertions | CI and unit tests check detector quality, adverse scenarios, platform adapters, rendering logic, and architecture-sensitive defaults. | `tests/`, `.github/workflows/ci.yml` | ✓ |
| Controlled fault injection | Synthetic scenarios inject weak signals, noise, beat timing changes, and impulse-like faults without requiring real hardware. | `AdverseScenarios.cs`, `DetectorStressScenarioTests.cs`, `WatchSynthImpulseNoiseTests.cs` | ✓ |

### 3.5 Portability and Usability

| SAP tactic | How the project applies it | Evidence | Mark |
|---|---|---|---|
| Defer binding | RID-conditioned project references decide which platform adapter assembly is compiled into a RID-specific build/publish; `LiveAudioBackend` then selects the concrete worker at runtime with OS checks. Later than source design but earlier than runtime plugin loading. | `TimeGrapher.App.csproj`, `LiveAudioBackend.cs` | △ |
| Degradation | Linux device discovery falls back from PipeWire (`wpctl`) to ALSA (`arecord -l`) enumeration when PipeWire returns no devices; capture then runs whichever backend the chosen device maps to. The preferred USB watch mic volume is normalized to 35% at the platform-adapter boundary (`AudioCaptureWorker` through Windows endpoint volume, `LinuxLiveAudioWorker` through `wpctl set-volume`) so the same hardware starts from the same capture level on Windows and Raspberry Pi without leaking OS audio APIs into App/Core. The advisory signal-quality classifier degrades the same way: if the embedded ONNX model cannot be loaded, `OnnxSignalQualityClassifier.LoadOrElse` falls back to the heuristic Strategy so a model problem never crashes startup. Credential persistence also degrades to memory-only login when the OS credential-store probe fails, so the AI analysis flow can still run without writing secrets to plain text. | `LinuxLiveAudioWorker.cs`, `AudioCaptureWorker.cs`, `LiveAudioBackend.cs`, `OnnxSignalQualityClassifier.cs`, `AiCredentialStore.cs` | ✓ |
| Maintain UI consistency | Theme colors and graph palettes are centralized. Graph axis panels, reset controls, alert strips, acceptable bands, and the Scope Sweep fixed readout slot use shared rendering conventions so transient labels do not resize or overlap graph rows. | `App.axaml`, `PlotThemePalette.cs`, `PlotThemeHelper.cs`, `InfoTabRegistry.cs` | ✓ |
| Pause/resume | Worker pause gates allow run control without destroying the whole session state, while stop remains separately requestable. | `WorkerPauseGate.cs` | ✓ |
| Support user initiative | Knobs that do not change a run's structure stay adjustable mid-run instead of forcing a stop/restart: live-capture gain is forwarded to the worker during capture, and the simulation's Error Rate / Amplitude / Beat Error and per-cluster A/B/C signal sizes are re-read by the already-running synth stream (a pending value applied on the sim thread between fill blocks). BPH, sample rate, and the Realistic preset stay start-only because they fix the run's structure; the A/B/C scales additionally require Realistic (the simple packet ignores them). | `RunSessionController.cs`, `SimWorker.cs`, `WatchSynthStream.cs`, `MainWindowSelectionCoordinator.cs` | ✓ |

## 4. Design Patterns Actually Used

| Pattern | Application | Mark |
|---|---|---|
| Layers | App, Platform, Core, Verify form a directed layered structure. | ✓ |
| Adapter | Platform audio workers translate Windows/Linux APIs into Core/App audio contracts. AI credential adapters translate Windows Credential Manager and Linux Secret Service (`secret-tool`) behind the app credential-store contract. | ✓ |
| Factory | Audio backends, tab registrations, and recording writers are created through narrow factory boundaries. | ✓ |
| Strategy | Input modes, tab frame consumers, and the swappable signal-quality classifier (`ISignalQualityClassifier`) share stable interfaces but differ in implementation. The shipped Strategy is the on-device TinyML `OnnxSignalQualityClassifier` (`TimeGrapher.Inference` leaf); `HeuristicSignalQualityClassifier` is the dependency-free fallback. The composition root selects one behind the seam, so the ONNX runtime never leaks into Core. | ✓ |
| State | Run lifecycle behavior is delegated to explicit state classes. | ✓ |
| Command | View-model commands expose UI actions and CanExecute state. | ✓ |
| Observer | Workers raise frame/data/completion events; lifecycle code subscribes and detaches. | ✓ |
| Producer-Consumer | Audio input, analysis, rendering, and recording exchange bounded work items. | ✓ |
| Shared Data | `MasterAudioBuffer` is the synchronized ring between producers and the analysis consumer. | ✓ |
| MVVM | View-models own UI state and commands, but some orchestration remains in services and view adapters. | △ |
| Pipe-and-Filter | The DSP path is filter-like, but it is mostly a synchronous processing chain rather than separately scheduled pipe stages. | △ |
| Map-Reduce | Not applicable: the metric path is incremental streaming aggregation, not distributed map/reduce. | ✗ |

## 5. Deliberate Scope Corrections

These distinctions prevent overclaiming:

- `bound execution times` is only partial for analysis work. The code bounds work size and monitors lag; it does not prove a hard upper bound for every pass.
- Linux `Thread.Priority` should not be treated as a real-time guarantee. The important protection is bounded work and degradation, not OS scheduling promises.
- PipeWire-to-ALSA behavior is degradation, not full reconfiguration recovery.
- MVVM is a useful UI structure here, but the app is not pure MVVM. Services and view adapters still coordinate platform/UI-specific behavior.
- Map-Reduce is rejected because no shuffle, distributed split, or parallel reduce exists.

## 6. Presentation Summary

The three strongest architecture points to present are:

1. **Dependency direction is enforced, not just documented.** `Core` stays independent of UI and platform details, and CI guards that rule.
2. **The real-time UI is protected by latest-wins rendering and active-tab scheduling.** Intermediate frames can be dropped without losing cumulative metric history.
3. **Run-session tokens prevent stale callbacks from corrupting later runs.** This is the central reliability tactic for start/stop/pause behavior.
