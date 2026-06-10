# Data Model View

TimeGrapherNet은 별도 데이터베이스를 사용하지 않는다. 따라서 전통적인 persisted domain element는 WAV 파일이며, 나머지는 실행 중 생성·전달·렌더링되는 도메인 데이터 구조다. 이 다이어그램은 프로젝트가 조작하는 주요 데이터 엔티티와 1:1, 1:n, 집합/집약, 일반화/특수화 관계를 함께 보여준다.

```mermaid
classDiagram
direction TB

class AnalysisRun {
    +ulong SessionId
    +RunMode Mode
}

class AnalysisRunSettings {
    +int SampleRate
    +double LiftAngle
    +bool AutoBph
    +int ManualBph
    +double HpfCutoffHz
    +int SoundImageSize
}

class AudioSource {
    <<abstract>>
    +string SourceKind
}

class LiveAudioSource {
    +double Gain
}

class PlaybackSource {
    +string FilePath
}

class SimSource {
    +double Bph
    +double RateError
    +double BeatError
}

class LiveAudioDevice {
    +int Number
    +string Name
}

class WatchSynthStreamConfig {
    +uint SampleRateHz
    +double Bph
    +double RateErrorSPerDay
    +double BeatErrorMs
    +double WatchAmplitudeDegrees
}

class WavFile {
    +string Path
    +bytes RIFF_WAVE_Data
}

class WavFormatInfo {
    +ushort AudioFormat
    +ushort NumChannels
    +int SampleRate
    +ushort BitsPerSample
    +long DataOffset
    +uint DataSize
}

class WavData {
    +int SampleRate
    +float Samples
}

class MasterAudioBuffer {
    +int Channels
    +int SecondsOfBuffer
    +float Samples
    +ulong TotalSamplesWritten
}

class AnalysisFrame {
    +ulong SessionId
    +ulong SourceId
    +ulong SourceSampleEnd
    +int SampleRate
    +bool InputOverrun
}

class GraphSeriesFrame {
    +string Id
    +double X
    +double Y
    +bool Replace
}

class ScopeMarker {
    <<abstract>>
    +double X
    +uint Color
}

class ScopeVerticalMarker {
    +double Height
}

class ScopeHorizontalMarker {
    +double XLeft
    +double XRight
    +double Length
}

class ScopeTextMarker {
    +string Text
    +MarkerTextAlignment Alignment
}

class WatchMetricsUpdate {
    +double TicRatePoints
    +double TocRatePoints
    +string ResultsText
    +string CMarkerText
    +BeatTimingSample BeatTimingSample
    +AmplitudeSample AmplitudeSample
    +DerivedTimingMeasures DerivedMeasures
}

class BeatTimingSample {
    +ulong BeatNumber
    +double TimeS
    +bool IsTic
    +double RateErrorMs
    +double RateSPerDay
    +double BeatErrorSignedMs
}

class AmplitudeSample {
    +double TimeS
    +double InstantDeg
    +double PairAverageDeg
}

class DerivedTimingMeasures {
    +double DiffTicTacMs
    +double DiffPeriodMs
    +double AvgPeriodMs
}

class BeatMetricsHistorySnapshot {
    +ulong Version
    +MetricsHistorySeries Rate
    +MetricsHistorySeries Amplitude
    +MetricsHistorySeries BeatError
    +DerivedTimingMeasures Derived
    +StatsSummary RateStats
    +StatsSummary AmplitudeStats
    +double LatestTimeS
}

class StatsSummary {
    +bool Valid
    +double Min
    +double Max
    +double Mean
    +double Sigma
    +long Count
}

class MetricsHistorySeries {
    +double X
    +double Y
    +double YMin
    +double YMax
}

class PixelBuffer {
    +int Width
    +int Height
    +uint Pixels
}

class TgConfig {
    +double SampleRate
    +TgBphMode BphMode
    +int ManualBph
    +double HpfCutoffHz
}

class TgResult {
    +TgSyncStatus SyncStatus
    +int DetectedBph
    +double MeasuredPeriodS
    +float ProcessedPcm
}

class TgEvent {
    <<abstract>>
    +double TimeSeconds
    +ulong SampleIndex
    +float PeakValue
    +TgEventType Type
}

class AEvent {
    +TgEventType A
}

class CEvent {
    +TgEventType C
    +double OnsetTimeSeconds
    +bool OnsetValid
}

AudioSource <|-- LiveAudioSource
AudioSource <|-- PlaybackSource
AudioSource <|-- SimSource
ScopeMarker <|-- ScopeVerticalMarker
ScopeMarker <|-- ScopeHorizontalMarker
ScopeMarker <|-- ScopeTextMarker
TgEvent <|-- AEvent
TgEvent <|-- CEvent

AnalysisRun "1" *-- "1" AnalysisRunSettings : configured by
AnalysisRun "1" *-- "1" AudioSource : selects
AnalysisRun "1" *-- "1" MasterAudioBuffer : owns
AnalysisRun "1" o-- "0..*" AnalysisFrame : produces

LiveAudioSource "1" --> "1" LiveAudioDevice : captures from
PlaybackSource "1" --> "1" WavFile : reads
SimSource "1" *-- "1" WatchSynthStreamConfig : uses

WavFile "1" *-- "1" WavFormatInfo : contains format
WavFile "1" --> "0..1" WavData : decoded as
WavData "1" --> "0..*" MasterAudioBuffer : supplies samples to
MasterAudioBuffer "1" --> "0..*" TgResult : analyzed into

TgConfig "1" --> "0..*" TgResult : configures detection
TgResult "1" *-- "0..*" TgEvent : contains

AnalysisFrame "1" *-- "0..*" GraphSeriesFrame : contains scope/rate series
AnalysisFrame "1" *-- "0..*" ScopeMarker : contains markers
AnalysisFrame "1" *-- "1" WatchMetricsUpdate : contains metrics
AnalysisFrame "1" o-- "0..1" PixelBuffer : contains sound image
AnalysisFrame "1" o-- "0..1" BeatMetricsHistorySnapshot : shares cumulative history

WatchMetricsUpdate "1" o-- "0..1" BeatTimingSample : per A event
WatchMetricsUpdate "1" o-- "0..1" AmplitudeSample : per C event
WatchMetricsUpdate "1" o-- "0..1" DerivedTimingMeasures : per A event
BeatMetricsHistorySnapshot "1" *-- "3" MetricsHistorySeries : rate/amplitude/beat error
BeatMetricsHistorySnapshot "1" *-- "2" StatsSummary : running stability stats
```

## Entity summary

| Entity | Source in project | Meaning |
|---|---|---|
| `WavFile`, `WavFormatInfo`, `WavData` | `Core.AudioIo` | Persisted or decoded audio data used for playback, recording, and verification |
| `AnalysisRunSettings` | `TimeGrapher.App` | User-selected run parameters converted into analysis worker configuration |
| `AudioSource` specializations | App run modes and Core workers | Live microphone, WAV playback, or synthetic signal input |
| `MasterAudioBuffer` | `Core.Shared` | Shared mono float ring buffer between input workers and analysis |
| `TgConfig`, `TgResult`, `TgEvent` | `Core.Detection` | Detector configuration, sync state, processed PCM, and tick/tock events |
| `AnalysisFrame` | `Core.Shared` | One UI update payload produced by an analysis pass |
| `GraphSeriesFrame`, `ScopeMarker`, `WatchMetricsUpdate`, `PixelBuffer` | `Core.Shared` | Data displayed as scope/rate graphs, markers, numeric results, and sound-print image |
| `BeatTimingSample`, `AmplitudeSample`, `DerivedTimingMeasures` | `Core.Shared` | Machine-readable per-beat values (rate error, signed beat error, amplitude, DiffTicTac/DiffPeriod/AvgPeriod) emitted per A/C event |
| `BeatMetricsHistorySnapshot`, `MetricsHistorySeries` | `Core.Shared` (built by `Core.Metrics.BeatMetricsHistory`) | Immutable cumulative history of rate/amplitude/beat-error series shared across frames; survives latest-wins frame coalescing |
| `StatsSummary` | `Core.Shared` (fed by `Core.Metrics.RunningStats`) | Running min/max/mean/population-σ since start for rate and amplitude — exact per-beat statistics independent of series decimation (Vario display) |

## Relationship notes

| Relationship type | Representation in this project |
|---|---|
| 1:1 | One `AnalysisRun` has one `AnalysisRunSettings`, one selected `AudioSource`, and one `MasterAudioBuffer` |
| 1:n | One `AnalysisRun` produces many `AnalysisFrame` objects; one `TgResult` contains many `TgEvent` objects; one `AnalysisFrame` contains many graph series and markers |
| n:n | No native persisted many-to-many relationship exists because the app has no database and most runtime data is owned by a single run/frame |
| Generalization / specialization | `AudioSource` specializes into live/playback/sim sources; `TgEvent` specializes into A and C events; `ScopeMarker` specializes into vertical/horizontal/text markers |
| Aggregation / composition | `AnalysisFrame` is composed from graph series, markers, metrics, and optional sound image; `WavFile` contains format metadata and can be decoded into `WavData`; `BeatMetricsHistorySnapshot` aggregates three `MetricsHistorySeries` and is shared (aggregation, not owned) by many frames |
