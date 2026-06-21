# Run Lifecycle State Machine

> Korean version: [Run lifecycle state machine](RUN_LIFECYCLE_STATE_MACHINE.md)

This view presents the `RunCommandService` control states (State Pattern) and `MainWindowViewModel.RunState` (`RunUiState`) as a state machine. Object call order is covered by the [sequence view](RUN_LIFECYCLE_SEQUENCE_VIEW_LEVELED_EN.md); control-state transitions are covered here.

![Run state machine](assets/uml25/run-lifecycle-state.svg)

Edit source: [state.drawio](state.drawio)

## 1. Scope

The base state value for this state machine is `RunUiState`. `RunCommandService` selects the state object matching the current `RunUiState` (`StoppedState`, `RunningState`, and so on), and each state object allows or ignores `StartAsync`, `TogglePause`, `StopRunWithoutReset`, `StopRunAndRefreshDevices`, and `Reset`.

Actual worker creation, worker stop, recording close, and device restoration are delegated to the View implementation through the `IRunCommandOperations` port. This view therefore shows which state the app moves to, while detailed input worker and analysis worker calls remain in the sequence view.

## 2. States

| State | Code-level meaning |
| --- | --- |
| `Stopped` | Default non-measuring state. Run settings can be changed, and `StartAsync` is allowed. |
| `Starting` | Start is in progress. Duplicate start, stop, and reset commands are ignored. |
| `Running` | Input and analysis workers are active. Pause or stop intent is allowed. |
| `Paused` | Workers remain alive, but input is held by the pause gate. Resume or Reset is allowed. |
| `Stopping` | Stop intent is being processed. If stop is not finished yet, the Stop/Reset retry surface remains available. |
| `StopFailed` | Full stop failed because worker stop timed out or recording close failed. Stop/Reset retry repeats the same pending intent. |

## 3. Transitions

| From -> To | Trigger / condition | Code basis |
| --- | --- | --- |
| initial -> `Stopped` | Default value when the app starts | `MainWindowViewModel._runState = RunUiState.Stopped` |
| `Stopped` -> `Starting` | Start from the Play/Pause button | `StoppedState.StartAsync` -> `StartFromStoppedAsync` -> `SetStarting` |
| `Starting` -> `Running` | Live / Playback / Simulation starts successfully | `StartLiveAsync` / `StartPlaybackAsync` / `StartSimulationAsync` call View-side `SetGuiRunMode` |
| `Starting` -> `Stopped` | Start fails, or the user cancels Playback file selection | `CleanupFailedStart`, `ShowStartFailureAsync`, `SetStopped` |
| `Running` -> `Paused` | Pause | `RunningState.TogglePause` -> `PauseRunning` -> `SetWorkersPaused(true)` |
| `Paused` -> `Running` | Resume | `PausedState.TogglePause` -> `ResumePaused` -> `SetWorkersPaused(false)` |
| `Running` -> `Stopping` | External stop intent: live capture ended, playback/simulation natural completion, and similar paths | `StopRunWithoutReset`, `StopRunAndRefreshDevices`, `CompletePlaybackOrSimulationRun` |
| `Paused` -> `Stopping` | Reset | `PausedState.Reset` -> `ResetFromPaused` -> `BeginStop(ResetAfterStop)` |
| `Stopping` -> `Stopped` | Worker stop, analysis stop, and audio close succeed | `CompleteStop`, `SetStopped` |
| `Stopping` -> `StopFailed` | Worker stop timeout or recording close failure | `RunCommandStopOutcome.Stopping` or `CloseAudio() == false` |
| `StopFailed` -> `Stopping` | Stop or Reset retry | `StopFailedState.StopRunWithoutReset/Reset` -> `RetryPendingStop` |

The diagram separates `Running` -> `Stopping` as `Stop` and `Paused` -> `Stopping` as `Reset`. In the actual UI, Reset is disabled while `Running`; the user reset path enters through Pause and then Reset. Stops from `Running` are driven by internal stop requests, live capture end, and playback/simulation completion.

## 4. Stop Intent

`RunCommandService` keeps `_pendingStopIntent` so that the user's intent is not lost when stopping fails.

| Intent | Entry path | Post-success behavior |
| --- | --- | --- |
| `StopOnly` | General stop request | Invalidate session, restore Playback/Simulation audio state if needed, then `Stopped` |
| `RefreshDevicesAfterStop` | Device list refresh after abnormal Live capture end | `Stopped`, then `RefreshDevices` |
| `ResetAfterStop` | Reset from `Paused` | After transition to `Stopped`, run `ResetRunState`, `RefreshDevices`, Status=`Reset` |

Retrying from `StopFailed` repeats the stored intent. If no pending intent exists, it falls back to `StopOnly`.

## 5. Natural Completion and Failure Recovery

When a Playback / Simulation worker naturally finishes because the file or synthesized input ends, the View runs `CompletePlaybackOrSimulationRun`. This path does not go through the `RunCommandService` button command, but it still uses the same `RunUiState`.

- Completion begins by switching to `Stopping` through `SetGuiStoppingMode`.
- If input worker stop, analysis worker `CompleteInput`, and audio close succeed, the app moves to `Stopped` through `SetGuiStopMode`.
- If worker stop times out or audio close fails, the app moves to `StopFailed` and waits for recovery.

If Live capture ends unexpectedly, it enters the `RunCommandService` path through `StopRunAndRefreshDevices`, and refreshes the device list after stop succeeds. Window close is a shutdown path where recovery UI disappears, so it is not part of the retry surface in this state machine.

## 6. UI-Derived State

`RunUiState` also determines button enablement and labels.

| UI property | State condition |
| --- | --- |
| Run settings enabled | `Stopped` |
| Play/Pause enabled | `Stopped`, `Running`, `Paused` |
| Reset enabled | `Stopped`, `Paused`, `Stopping`, `StopFailed` |
| Play/Pause label | `Stopped`=`Start`, `Paused`=`Resume`, otherwise=`Pause` |
| Review bar enabled | `Paused` |

When leaving `Paused`, the review cursor is cleared first so stale scrub markers do not remain after stop or resume.

## 7. Basis Modules

| Responsibility | Code location |
| --- | --- |
| State value and UI-derived properties | `src/TimeGrapher.App/ViewModels/MainWindowViewModel.cs` (`RunUiState`) |
| State objects and command allow/ignore rules | `src/TimeGrapher.App/Services/RunCommandService.States.cs` |
| Start/stop transitions and pending stop intent | `src/TimeGrapher.App/Services/RunCommandService.cs` |
| Worker/audio/session operation port | `src/TimeGrapher.App/Services/IRunCommandOperations.cs` |
| View-side port implementation | `src/TimeGrapher.App/Views/MainWindow.RunCommandOperations.cs` |
| Live/Playback/Simulation start and natural completion | `src/TimeGrapher.App/Views/MainWindow.RunLifecycle.cs` |
| Input/analysis worker stop outcome | `src/TimeGrapher.App/Services/RunSessionController.cs` |

## 8. Notation

- Filled circle: initial pseudostate.
- Rounded rectangle: `RunUiState` state.
- Arrow: state transition. The label summarizes a user command or an internal stop/completion intent.

By SAP tactics terminology, the run state objects implement the State Pattern. `RunState = X` context scattered across the sequence view refers to the states in this state machine.
