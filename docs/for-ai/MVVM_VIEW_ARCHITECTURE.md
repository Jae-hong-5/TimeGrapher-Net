# MVVM View Architecture

이 문서는 TimeGrapher-Net의 Avalonia UI 계층을 MVVM 관점에서 설명하기 위한
뷰 아키텍처 다이어그램이다. 발표나 리뷰에서는 View, ViewModel, Model의
역할 분리를 중심으로 설명한다. 이 프로젝트에는 별도 `Models` 폴더가 없으며,
MVVM의 Model 역할은 애플리케이션 서비스, Core 도메인 로직, Platform 구현에
분산되어 있다.

## Architecture Diagram

```mermaid
flowchart LR
    subgraph ViewLayer["View Layer - Avalonia Views"]
        direction TB
        MainView["MainWindow.axaml<br/>Main UI layout"]
        MainCodeBehind["MainWindow.axaml.cs<br/>run orchestration,<br/>tab routing"]
        TabViews["InfoTabRegistry"]
        RenderViews["GraphFrameRenderer"]
    end

    subgraph ViewModelLayer["ViewModel Layer"]
        direction TB
        MainVM["MainWindowViewModel<br/>UI state, bindings"]
        Commands["Command<br/>reset, review controls"]
    end

    subgraph ModelLayer["Model Layer - Services + Core + Platform"]
    subgraph ServiceLayer["Application Services"]
        direction TB
        RunCommand["RunCommandService<br/>run-state transitions"]
        RunSession["RunSessionController"]
        Recording["RecordingSessionService / PlaybackFileService"]
    end

    subgraph CoreLayer["Core / Platform Model"]
        direction TB
        Analysis["TimeGrapher.Core.Analysis<br/>beat analysis and metrics"]
        Detection["TimeGrapher.Core.Detection<br/>detector logic"]
        AudioIo["TimeGrapher.Core.AudioIo<br/>audio input contracts"]
        Platform["TimeGrapher.Platform.*<br/>platform-specific capture"]
    end
    end

    style ViewLayer fill:#eaf2ff,fill-opacity:0.35,stroke:#2563eb,stroke-width:2px,color:#111827
    style ViewModelLayer fill:#edf8ee,fill-opacity:0.35,stroke:#2f855a,stroke-width:2px,color:#111827
    style ModelLayer fill:#fff3df,fill-opacity:0.35,stroke:#b26a00,stroke-width:2px,color:#111827
    style ServiceLayer fill:#fffaf0,fill-opacity:0.28,stroke:#d97706,stroke-width:1px,color:#111827
    style CoreLayer fill:#f7ecff,fill-opacity:0.28,stroke:#7c3aed,stroke-width:1px,color:#111827
    linkStyle default stroke:#111827,stroke-width:2px

    MainView -->|"Binding"| MainVM
    TabViews -->|"Binding"| MainVM

    MainVM -->|"exposes"| Commands
    Commands -->|"invoke callbacks"| MainCodeBehind

    MainCodeBehind -->|"coordinates"| RunCommand
    MainCodeBehind -->|"starts session"| RunSession
    MainCodeBehind -->|"recording / playback"| Recording
    MainCodeBehind -->|"routes frames"| TabViews
    MainCodeBehind -->|"updates plots"| RenderViews

    MainVM -->|"commands and state requests"| RunCommand
    RunCommand -->|"run state"| MainVM
    RunSession -->|"metrics state"| MainVM
    RunSession -->|"starts analysis"| Analysis
    Platform -->|"captured samples"| AudioIo
    AudioIo -->|"analysis input"| Analysis
    Detection -->|"beat events"| Analysis
    Analysis -->|"AnalysisFrame"| RunSession
    RunSession -->|"frame callback"| MainCodeBehind
    MainCodeBehind -->|"status, review state"| MainVM
```

## Responsibility Summary

| Layer | Main responsibility | Representative files |
| --- | --- | --- |
| View | UI layout, window lifecycle, tab routing, rendering bridge | `MainWindow.axaml`, `MainWindow.axaml.cs` |
| ViewModel | UI state, binding properties, commands, review controls | `MainWindowViewModel.cs`, `RelayCommand.cs`, `AsyncRelayCommand.cs` |
| Model | Runtime state, application behavior, domain analysis, audio contracts, platform capture | `RunCommandService.cs`, `RunSessionController.cs`, `TimeGrapher.Core.*`, `TimeGrapher.Platform.*` |
| Application Services | Run-state transitions, analysis session lifecycle, recording, playback | `RunCommandService.cs`, `RunSessionController.cs`, `RecordingSessionService.cs`, `PlaybackFileService.cs` |
| Rendering / Tabs | Graph view registration, active-tab rendering, plot updates | `InfoTabRegistry.cs`, `GraphFrameRenderer.cs`, renderer classes under `Rendering/` |
| Core / Platform Model | Audio contracts, capture workers, detector and analysis logic | `TimeGrapher.Core.*`, `TimeGrapher.Platform.*` |

## Presentation Description

TimeGrapher-Net uses the MVVM pattern in the Avalonia UI layer. Views define
the interface and bind to `MainWindowViewModel`; the ViewModel owns UI state
and commands; the Model layer is implemented by application Services plus Core
and Platform modules. Services coordinate run lifecycle, analysis sessions,
recording, and playback, while Core and Platform provide analysis, detection,
audio, and capture behavior.
