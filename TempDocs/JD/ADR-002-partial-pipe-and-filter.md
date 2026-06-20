# ADR 2: Use Partial Pipe-and-Filter for Real-Time Audio Analysis

## Status

Proposed

## Context

TimeGrapher receives watch audio and turns it into rate, amplitude, beat error, beat-noise traces, and other graph outputs. The runtime path is naturally staged:

```text
audio input -> HPF -> envelope -> detector -> metrics/projectors -> rendering
```

The system also has a real-time constraint. At 28800 BPH, one beat arrives every 125 ms. The analysis path must avoid accumulating backlog, and UI rendering must not block detection.

A textbook Pipe-and-Filter architecture could make each stage an independent filter connected by queues or streams. That would make the pattern visually clear, but it would also add synchronization, queueing, allocation, and latency costs to the hottest DSP path.

## Decision

Use Pipe-and-Filter as a partial architectural style for the analysis flow, not as a fully concurrent pipeline.

The core DSP path remains a synchronous staged chain inside the analysis worker. Each stage has a clear responsibility, but the stages are not all split into independent threads or queues.

Concurrent boundaries are used only where they protect real-time behavior:

- input worker -> bounded audio/ring buffer -> analysis worker
- analysis worker -> bounded recording queue
- analysis frame -> latest-wins UI render scheduler

## Rationale

This decision keeps the useful part of Pipe-and-Filter: staged transformation with understandable responsibilities.

It avoids the costly part for this system: making every stage a separate concurrent component. For TimeGrapher, extra queues between HPF, envelope, detector, and metrics would increase latency and synchronization overhead without a clear benefit, because these stages are cheap streaming operations that must stay within the beat budget.

The decision also keeps the documentation honest. The code has a staged data-flow shape, but it is not a full independent-filter pipeline. Therefore `SAP_TACTICS_ANALYSIS.md` should describe Pipe-and-Filter as partial application (`△`), not full application.

## Consequences

Positive:

- The analysis path is easier to explain as staged transformation.
- Each stage can still be reasoned about and tested separately.
- The hottest DSP path avoids unnecessary queue and thread overhead.
- The architecture documentation matches the actual code instead of overstating the pattern.

Negative / trade-offs:

- Individual filters are not independently deployable runtime components.
- The internal DSP stages do not provide parallel speedup by themselves.
- The documentation must clearly say "partial Pipe-and-Filter" to avoid confusing reviewers.

Follow-up documentation work:

- Link this ADR from the module uses view and SAP tactics analysis.
- Keep the Pipe-and-Filter row in `SAP_TACTICS_ANALYSIS.md` marked as partial (`△`).
