# MVVM 뷰 아키텍처 (의존성 뷰)

이 문서는 순수 MVVM 리팩토링 이후 `TimeGrapher.App`의 UI 계층을 **의존성(depends-on)
뷰**로 제시한다. `A --> B`는 "A가 *컴파일 시점*에 B의 타입을 알고 참조한다"는 뜻이다.
이 그래프는 **비순환이며 안쪽(View→ViewModel→Model)으로만** 향한다 — MVVM의 정의적
성질이 여기서 드러난다.

핵심: **ViewModel은 View·Avalonia에 대한 컴파일 의존이 없다**(`ViewModelPurityTests`가
잠금). 런타임에는 바인딩과 `PropertyChanged`로 데이터가 ViewModel→View로 흐르고
서비스가 View의 실행 본문을 되부르지만, 이는 모두 **바인딩·인터페이스로 디커플링된
제어 역전**이라 의존 엣지를 만들지 않는다. 그래서 의존성 뷰는 단방향으로 유지된다.

이 프로젝트에는 별도 `Models` 폴더가 없다. MVVM의 Model 역할은 애플리케이션 서비스,
`TimeGrapher.Core` 도메인 로직, `TimeGrapher.Platform.*` 구현에 분산되어 있다.

## 의존성 뷰 (depends-on, 컴파일 시점)

```mermaid
flowchart TB
    subgraph ViewLayer["View 계층 (Avalonia)"]
        direction TB
        VView["MainWindow<br/>.axaml + code-behind"]
        VAdapters["뷰 측 어댑터 · 컨버터<br/>GraphAcceptBandOperations · LiveAudioDeviceBackend<br/>UiThreadDispatcher · RunCommandOperations<br/>ReviewSliderMarginConverter"]
        VTabs["Tabs · Rendering<br/>InfoTabRegistry · GraphFrameRenderer"]
    end

    subgraph ViewModelLayer["ViewModel 계층 (Avalonia 무의존)"]
        direction TB
        VM["MainWindowViewModel<br/>RelayCommand · AsyncRelayCommand"]
        Runner["IRunCommandRunner<br/>(seam, ViewModels)"]
        NoDep["✗ ViewModel → View 의존 없음<br/>✗ ViewModel → Avalonia 의존 없음<br/>(ViewModelPurityTests가 잠금)"]
    end

    subgraph ModelLayer["Model 계층 (Services + Core + Platform)"]
        subgraph Services["Application Services"]
            direction TB
            Boot["MainWindowBootstrapper<br/>composition root"]
            Ctrls["구독 컨트롤러<br/>AcceptBandController · RunControlController<br/>MeasurementLogController · MainWindowSelectionCoordinator"]
            Owners["명시 호출 소유자<br/>AudioDeviceController · AudioSelectionState<br/>RunSessionController"]
            RunCmd["RunCommandService"]
            Presenter["AnalysisFramePresenter"]
            Seams["Service 시밍 인터페이스<br/>IAcceptBandOperations · IRunSessionControls · IRunCommandPause<br/>IMeasurementResultSink · IAudioDeviceBackend · IUiDispatcher<br/>ISelectionEventGate · IRunCommandOperations"]
        end
        subgraph CorePlatform["Core / Platform"]
            direction TB
            Core["TimeGrapher.Core<br/>Analysis · Detection · Metrics<br/>AudioIo · Shared · Sim · Imaging"]
            Platform["TimeGrapher.Platform.*<br/>(RID 조건부)"]
        end
    end

    style ViewLayer fill:#eaf2ff,fill-opacity:0.35,stroke:#2563eb,stroke-width:2px,color:#111827
    style ViewModelLayer fill:#edf8ee,fill-opacity:0.35,stroke:#2f855a,stroke-width:2px,color:#111827
    style ModelLayer fill:#fff3df,fill-opacity:0.35,stroke:#b26a00,stroke-width:2px,color:#111827
    style Services fill:#fffaf0,fill-opacity:0.28,stroke:#d97706,stroke-width:1px,color:#111827
    style CorePlatform fill:#f7ecff,fill-opacity:0.28,stroke:#7c3aed,stroke-width:1px,color:#111827
    style NoDep fill:#fdecec,stroke:#c0392b,stroke-width:1px,color:#7b241c,stroke-dasharray:4 3
    linkStyle default stroke:#111827,stroke-width:1.5px

    VView --> VM
    VView --> Boot
    VView --> Owners
    VAdapters -. "구현(realize)" .-> Seams
    VTabs --> VM
    VTabs --> Core

    VM --> Runner
    VM --> Core

    Boot --> VM
    Boot --> Ctrls
    Boot --> RunCmd
    Boot --> Presenter
    Boot --> Core
    Ctrls --> VM
    Ctrls --> Seams
    Owners --> Seams
    Owners --> Core
    RunCmd -. "구현(realize)" .-> Runner
    RunCmd --> Seams
    Presenter --> VM
    Presenter --> Core
    Platform --> Core
```

**읽는 법.** 모든 실선 엣지는 안쪽(View→ViewModel→Model)으로만 향한다. ViewModel에서
나가는 의존은 자기 네임스페이스의 `IRunCommandRunner`와 `Core`의 DTO(`Analysis`/`Shared`)
뿐이며 — 붉은 박스가 강조하듯 **View·Avalonia로 향하는 엣지는 없다**. 서비스가 View의
실행 본문을 되부르는 관계는 `RunCommandService → IRunCommandOperations`(서비스가 인터페이스에
의존)와 `VAdapters ⟶ 구현`(View가 인터페이스를 실현)으로 **역전**되어, `Services → View`
컴파일 엣지가 생기지 않는다. 컨트롤러는 `MainWindowViewModel`을 참조해 구독하지만
(구독자 패턴), 그 의존은 ViewModel을 향하지 View를 향하지 않는다.

## 책임 요약

| 계층 | 주 책임 | 대표 파일 |
| --- | --- | --- |
| View | UI 레이아웃·창 수명주기, 프레임 라우팅, 실행 본문(잔여물), 렌더 브리지 | `MainWindow.axaml`, `MainWindow.*.cs`, `*Operations`, `ReviewSliderMarginConverter.cs` |
| ViewModel | UI 상태·바인딩 속성·커맨드 (Avalonia 무의존) | `MainWindowViewModel.cs`, `IRunCommandRunner.cs`, `RelayCommand.cs`, `AsyncRelayCommand.cs` |
| Application Services | composition root, 구독 컨트롤러, 실행/세션/측정 로그/장치 열거, 프레임→VM 프레젠터, 시밍 인터페이스 | `MainWindowBootstrapper.cs`, `AcceptBandController.cs`, `RunControlController.cs`, `MeasurementLogController.cs`, `AudioDeviceController.cs`, `RunSessionController.cs`, `RunCommandService.cs`, `AnalysisFramePresenter.cs`, `I*.cs` |
| Rendering / Tabs | 그래프 등록·활성 탭 렌더·플롯 갱신 | `InfoTabRegistry.cs`, `GraphFrameRenderer.cs`, `Rendering/*` |
| Core / Platform Model | 오디오 계약·캡처 워커·검출·분석 | `TimeGrapher.Core.*`, `TimeGrapher.Platform.*` |

## 발표용 설명

순수 MVVM 리팩토링 이후 `MainWindowViewModel`은 UI 프레임워크 타입을 전혀 갖지 않으며
(`ViewModelPurityTests`가 잠금), 의존성 뷰는 View→ViewModel→Model의 비순환 안쪽 방향만
보인다. View가 서비스의 명령을 받아 실행 본문을 수행하던 옛 구조는 컨트롤러·시밍
인터페이스로 분해되어, 서비스→View 역의존이 인터페이스로 역전됐다. 런타임 데이터는
바인딩·`PropertyChanged`로 양방향으로 흐르지만 이는 디커플된 제어 역전이라 의존 엣지를
만들지 않으므로, 의존성은 단방향으로 유지된다 — 이것이 MVVM의 핵심이다.

> 받아들인 잔여물: `RunCommandService`가 `IRunCommandOperations`(View 중첩)로 호출하는
> 실행 본문(`LiveStart`/`PlaybackStart`/`SimStart`, `BuildRunSettings`)은 행위 보존과
> 아키텍처 변경 최소화를 위해 View에 남겼다. 컴파일 의존은 역전됐고 본문만 code-behind에
> 있다. 자세한 모듈 엣지는 [`MODULE_USES_VIEW.md`](MODULE_USES_VIEW.md)를 참조한다.
