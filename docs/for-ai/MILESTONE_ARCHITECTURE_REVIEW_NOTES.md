# Milestone Architecture Review Notes

These notes review the current `TimeGrapher-Docs/Milestone/en` architecture material from the perspective of the course feedback and the current source architecture. They are kept in the source repo so the implementation branch records the rationale and recommended presentation follow-up.

## Source Material Reviewed

- `D:\swa\TimeGrapher-Docs\Milestone\en\5-Architectural-View.md`
- `D:\swa\TimeGrapher-Docs\Milestone\en\Milestone2 Review Q&A.md`
- `D:\swa\TimeGrapher-Docs\Milestone\en\2-Architectural-Drivers.md`, especially QAS-5 modifiability
- `D:\swa\TimeGrapher-Docs\Milestone\en\3-Risk-Assessment.md`, especially R-17 TinyML and extension risk
- `D:\swa\TimeGrapher-Docs\Milestone\en\4-Planned-Experiments.md`, especially EXP-03 and EXP-04

## What Works Well

- The architectural view includes the right view types for the course: MVVM/module uses, runtime sequence/state behavior, deployment, and a related layered view.
- The one-way dependency rule is clear and traceable to the actual source structure: App and platform adapters depend on Core, while Core stays UI/platform independent.
- The runtime behavior is not only drawn but explained through concrete mechanisms: `RunCommandService`, `RunSessionController`, input workers, `AnalysisWorker`, shared buffer, and UI rendering.
- The Q&A already connects architecture decisions to quality attributes: QAS-2 for real-time performance, QAS-4 for consistency, QAS-5 for modifiability, and QAS-6 for touchscreen usability.
- The TinyML decision is correctly treated as unresolved risk rather than assumed scope. R-17 and EXP-04 keep adoption conditional on latency and trustworthiness evidence.

## Gaps To Address Before Presentation

1. **Purpose of each view is too implicit.** The document lists each view, but it should say what stakeholder question each view answers. Example: the module uses view answers "what can depend on what?", the runtime C&C view answers "how does one audio block become visible output without blocking?", and the deployment view answers "where do OS/hardware dependencies enter?"

2. **Relationships among views need an explicit bridge.** The current text presents views mostly one after another. Add a short mapping that says the layered view defines the allowed dependency rule, the module uses view shows the code-level realization of that rule, the MVVM view zooms into App responsibility separation, and the sequence/state views show how those static elements collaborate at runtime.

3. **QAS-5 evidence should be shown with a concrete change example.** The architectural drivers define "new graph/filter/measurement touches <= 1 existing module", but the presentation should demonstrate that with one current feature. Signal-quality overlay propagation is a useful example: Core emits `SignalQualityFlags`, App renders it in Beat Noise/Waveform Compare/Escapement through rendering classes, and no platform adapter or Core dependency direction changes.

4. **TinyML extensibility needs a clearer insertion point.** The docs mention TinyML and the rule-based fallback, but presentation should identify the intended module boundary: a future inference module should remain outside Core and use the existing Core contracts/gate socket rather than adding UI/platform dependencies to Core.

5. **Error/warning UX should be tied to reliability.** R-07 and QAS-3 discuss weak/noisy signal handling. The presentation should show how the GUI now avoids silent misleading readings: warnings propagate into readouts/status guidance and diagnostic graph overlays, while suspicious C candidates are excluded from amplitude updates.

## Recommended Presentation Edits

Add a small "View Purpose and Traceability" table near the start of `5-Architectural-View.md`:

| View | Question answered | Trace to quality attribute |
|---|---|---|
| Layered View | Which dependencies are allowed? | QAS-5 modifiability, portability |
| Module Uses View | What does the code actually depend on? | QAS-5, C-3 cross-platform |
| MVVM View | How is UI responsibility separated? | QAS-5 testability/modifiability |
| Sequence/C&C View | How does data move during a run? | QAS-2 latency, QAS-4 consistency |
| State Machine View | Which user/run states are legal? | Reliability and usability |
| Deployment View | Where do hardware and OS boundaries enter? | C-3, QAS-2 |

Add a short "How the views connect" paragraph:

> The layered view sets the dependency rule. The module uses view verifies that the implementation follows that rule. The MVVM view zooms into the App layer to show UI responsibility separation. The sequence and state-machine views then show how those same modules collaborate during a measurement run. The deployment view places the same runtime path onto Windows/Raspberry Pi and microphone hardware boundaries.

Add a concrete modifiability example:

> Example: signal-quality propagation added user-visible warnings without changing platform adapters or reversing Core dependencies. Core publishes quality flags in shared DTOs, and App rendering consumers display the warning in the relevant graphs. This follows the QAS-5 extension rule because the change is localized to the shared contract, analysis/metrics logic, and the relevant rendering consumers.

## Source Architecture Implication

The current implementation remains consistent with the documented architecture:

- Core still owns signal interpretation and DTO contracts.
- App owns presentation state, warning text, overlay fade behavior, and recovery guidance.
- Platform adapters remain unchanged because acoustic quality classification is independent of OS capture APIs.
- Future TinyML should enter through an optional inference/gate boundary, not by making Core depend on UI, platform, or a model runtime package.