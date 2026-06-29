# Thread Data Flow View

이 문서는 TimeGrapherNet 런타임에서 입력, 분석, UI, 선택적 파일 출력 스레드가 데이터를 어떻게 주고받는지 요약한다. 핵심 구조는 샘플 payload를 이벤트로 직접 밀어 넣는 방식이 아니라, 입력 스레드가 `MasterAudioBuffer`에 쓰고 `DataReady`로 분석 스레드를 깨우는 Producer-Consumer + Shared Data 구조다.

## 1. Runtime thread/data-flow overview

```mermaid
flowchart LR
    UI["UI Thread<br/>run control + graph rendering"]
    Input["Input Worker Thread<br/>live / playback / simulation"]
    Buffer["MasterAudioBuffer<br/>shared ring buffer"]
    Analysis["AnalysisWorker Thread<br/>signal analysis + AnalysisFrame"]
    Writers["Optional Writer Threads<br/>WAV / measurement CSV / perf CSV"]

    UI -->|"start / stop / pause"| Input
    Input -->|"sample blocks"| Buffer
    Input -.->|"DataReady signal"| Analysis
    Buffer -->|"copied sample blocks"| Analysis
    Analysis -->|"AnalysisFrameReady(frame)"| UI
    Analysis -.->|"optional recording blocks"| Writers
    UI -.->|"optional displayed-frame logs"| Writers
```

## 2. Sequence view

```mermaid
sequenceDiagram
    participant U as UI thread
    participant I as Input worker thread
    participant B as MasterAudioBuffer
    participant A as AnalysisWorker thread
    participant W as Optional writer threads

    U->>I: Start run
    I->>B: Write sample blocks
    I-->>A: DataReady signal
    A->>B: Copy pending samples
    A-->>W: Optional recording blocks
    A-->>U: AnalysisFrameReady(frame)
    U-->>W: Optional displayed-frame log entries
```

## 3. Thread responsibilities and exchanged payloads

| Execution context | Owns | Sends | Receives |
|---|---|---|---|
| UI thread | Run controls, tab routing, graph rendering, status/warning presentation | Start/stop commands, UI posts, optional displayed-frame log entries | `AnalysisFrame` via dispatcher post |
| Live input callback / playback / simulation worker | Captured, decoded, or generated audio blocks | Float sample blocks into `MasterAudioBuffer`; `DataReady` signal | Pause/stop/live-adjust requests |
| `MasterAudioBuffer` | Synchronized sample history and write stamps | Copied analysis blocks | Raw float sample writes |
| `AnalysisWorker` thread | Detector, BPH sync, metrics, projectors, `AnalysisFrame` creation | `AnalysisFrameReady(frame)`, optional recording blocks | Wake-up signal and copied sample blocks |
| Optional writer threads | WAV recording, measurement CSV, performance CSV | Files on disk | Bounded queue entries |

## 4. Architectural reading

- Input to analysis is **Producer-Consumer + Shared Data**: sample blocks are stored in `MasterAudioBuffer`; `DataReady` is only a wake-up notification.
- Analysis is a **Pipeline / Dataflow**: copied blocks pass through detector, metrics, and projector stages.
- UI consumes a **Shared Read Model**: graph renderers read `AnalysisFrame` snapshots rather than recomputing raw audio analysis.
- UI scheduling applies the SAP performance tactics **Limit Event Response** and **Schedule Resources**: latest-wins frame coalescing and active-tab rendering keep the UI from accumulating stale graph work.
