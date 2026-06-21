# Run Lifecycle Sequence View (MVVM)

> Korean version: [Run lifecycle sequence view](RUN_LIFECYCLE_SEQUENCE_VIEW_LEVELED.md)

This behavior view shows which objects call each other from run start through measurement and shutdown. It reflects the **View / ViewModel / RunCommandService** collaboration after the MVC to MVVM refactor.

> Only the measurement flow is expanded into a Level 2 child view because it contains the recurring loop that needs the most detail.

## Document Roadmap

| Page | Contents |
| --- | --- |
| Level 1 | Run lifecycle overview |
| Level 2 | Measurement analysis loop, expanded from the Level 1 `ref` |

## 1. Primary Presentation · Level 1 · Run Lifecycle Overview

Level 1 keeps the whole lifecycle on one page. The detailed analysis loop is folded behind a `ref` and expanded in Level 2.

`RunState` transitions are not shown here. They are covered by the [state machine view](RUN_LIFECYCLE_STATE_MACHINE_EN.md).

![Level 1 run lifecycle overview](assets/uml25/run-lifecycle-seq-level1.svg)

## 2. Element Catalog

Roles and code references for each lifeline. `MasterAudioBuffer` and `Core pipeline` appear only in Level 2.

| Lifeline | MVVM layer | Responsibility | Code location |
| --- | --- | --- | --- |
| User | (actor) | User | - |
| View (`MainWindow`) | View | Receives UI events, renders, marshals to the UI thread, drives input/analysis worker lifecycle through `RunSessionController`, and implements the service's `IRunCommandOperations` callback port | `src/TimeGrapher.App/Views/MainWindow*.cs` |
| ViewModel (`MainWindowViewModel`) | ViewModel | Exposes `PlayPauseCommand`/`ResetCommand` and observable `RunState`/`StatusText`; does not call the domain directly | `src/TimeGrapher.App/ViewModels/MainWindowViewModel.cs` |
| RunCommandService | App service (State Pattern) | Orchestrates start/pause/stop, updates ViewModel state, and calls the View through `IRunCommandOperations` | `src/TimeGrapher.App/Services/RunCommandService*.cs` |
| RunSessionController | Model boundary | Owns run session tokens, input worker attach/stop, and analysis worker lifecycle | `src/TimeGrapher.App/Services/RunSessionController.cs` |
| Input worker | Model | Live=`AudioCaptureWorker`, Playback=`PlaybackWorker`, Simulation=`SimWorker` | `App.Audio` / `Core.AudioIo` / `Core.Sim` |
| MasterAudioBuffer | Model | Shared input/analysis audio ring buffer | `TimeGrapher.Core` |
| AnalysisWorker | Model | Analysis thread | `TimeGrapher.Core.Analysis` |
| Core pipeline | Model | Detection / Metrics / Projectors | `TimeGrapher.Core` |

## 3. Behavior · Level 2 · Measurement Analysis Loop

Level 2 expands the measurement `ref` from Level 1. The loop condition and timing constraint are shown inside the diagram.

![Level 2 measurement analysis loop](assets/uml25/run-lifecycle-seq-level2.svg)

## 4. Notation

The common notation follows the legend below.

![UML sequence diagram notation legend](assets/uml25/run-lifecycle-notation.svg)

Label rule: User-to-system arrows describe user intent or action; object-to-object arrows use operation signatures.

## 5. Variability

The only variation point is the input source: Live / Playback / Simulation. Runtime behavior branches on `CurrentMode`.

## 6. Design Rationale

- **Decision**: Split the UI state, commands, and run orchestration that had been mixed into the old MVC-style `MainWindow` into three MVVM roles: View for rendering/platform/session wiring, ViewModel for bindable state and commands, and RunCommandService for the State Pattern-based run state machine.
- **Rationale**: The separation improves modifiability and testability. The ViewModel can be unit-tested without a window because it does not call the domain directly. Service-to-View coupling is inverted through `IRunCommandRunner` for command bodies and `IRunCommandOperations` for service-to-View callbacks.
- **Rejected alternative**: The MVC remnant where the View injected command bodies into the ViewModel through `Func`/`Action` delegates. This was replaced by injecting `IRunCommandRunner`, so command bodies belong to the ViewModel-side command path.
- **Intentional exception**: Playback natural completion and application shutdown are handled directly by the View because they originate from worker completion or window closing callbacks, bypassing `RunCommandService`.

## 7. Related Views

- [State machine diagram](RUN_LIFECYCLE_STATE_MACHINE_EN.md) - companion view for the same run lifecycle, focused on control-state transitions (`RunUiState` + State Pattern).
- [Original run lifecycle sequence view](../RUN_LIFECYCLE_SEQUENCE_VIEW.md) - pre-leveling single sequence view, which may not reflect the MVVM split.
- Edit source: [sequence.drawio](sequence.drawio). Refresh SVGs with `python _drawio_to_svg.py`([_drawio_to_svg.py](_drawio_to_svg.py)); draw.io is not required. [_gen_sequence.py](_gen_sequence.py) is kept only as the initial skeleton generator and will overwrite manual drawio edits if rerun.
