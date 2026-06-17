# 실행 수명주기 시퀀스 뷰

이 문서는 TimeGrapherNet의 프로그램 실행부터 입력 모드 선택, 측정 시작, 측정 종료, 프로그램 종료까지의 대표 상호작용 trace를 sequence diagram으로 보여준다. 구조 전체를 모두 나열하는 문서가 아니라, `35 Behavior - intro, sequence diagram, activity diagram, BPMN.pdf`의 sequence diagram 기준처럼 객체 간 상호작용의 특정 흐름과 가능한 대안 경로를 함께 표현한다.

> 입력 모드는 코드 기준의 `RunCommandMode`에 맞춰 `Live`, `Playback`, `Simulation` 세 갈래로 둔다. 사용자 관점의 "측정 종료"는 현재 UI에서 별도 Stop 버튼이 아니라 실행 중단/리셋, Playback/Simulation 자연 종료, Live 캡처 비정상 종료, 창 종료 경로를 통해 수행된다.

## 시퀀스 다이어그램

```mermaid
%%{init: {"theme": "base", "themeVariables": {"activationBkgColor": "#bfdbfe", "activationBorderColor": "#1d4ed8"}}}%%
sequenceDiagram

    actor User as User
    participant App as "TimeGrapher.App<br/>Program/MainWindow"
    participant VM as "MainWindowViewModel"
    participant Cmd as "RunCommandService<br/>(State Pattern)"
    participant Ops as "RunCommandOperations"
    participant Sess as "RunSessionController"
    participant Backend as "LiveAudioBackend"
    participant Input as "IAudioInputWorker<br/>Live/Playback/Simulation"
    participant Buffer as "MasterAudioBuffer"
    participant Analysis as "AnalysisWorker"
    participant Core as "Core pipeline<br/>Detection/Metrics/Projectors"
    participant Render as "Render scheduler<br/>Frame router/tabs"
    participant Writer as "Optional WAV writer"

    User->>App: 프로그램 실행
    activate App
    App->>VM: ViewModel, commands, services 구성
    activate VM
    VM-->>App: 초기 UI 상태 준비
    deactivate VM
    App->>Backend: LoadAudioDevices()
    activate Backend
    Backend-->>App: Live devices + Playback + Simulation sources
    deactivate Backend
    App->>VM: 입력 장치와 샘플레이트 표시
    activate VM
    VM-->>App: 바인딩 상태 갱신
    deactivate VM
    deactivate App

    User->>VM: Start 선택
    activate VM
    VM->>App: PlayPauseCommand -> StartRunAsync()
    activate App
    App->>Cmd: StartAsync()
    activate Cmd
    Cmd->>VM: RunState = Starting<br/>Status = "Starting"
    activate VM
    VM-->>Cmd: 상태 갱신 완료
    deactivate VM
    Cmd->>Ops: CurrentMode 조회
    activate Ops
    Ops-->>Cmd: mode
    deactivate Ops

    alt Live mode
        Cmd->>Ops: ConfigureLiveAudio()
        activate Ops
        Ops->>Backend: ConfigurePreferredInput()
        activate Backend
        Backend-->>Ops: preferred input configured
        deactivate Backend
        Ops-->>Cmd: live audio configured
        deactivate Ops
        Cmd->>Ops: StartLiveAsync()
        activate Ops
        Ops->>App: LiveStart()
        activate App
        App->>App: 선택된 live device/rate 검증
        activate App
        deactivate App

        opt 사용자가 녹음 저장을 선택한 경우
            App->>Writer: RecordingSessionService.TryStartAsync()
            activate Writer
            Writer-->>App: ISampleWriter
            deactivate Writer
        end

        App->>Sess: PrepareInputRun(sampleRate)
        activate Sess
        Sess->>Sess: 새 runSessionToken 발급
        activate Sess
        deactivate Sess
        Sess->>Render: resetBeforeRun / clear stale frames
        activate Render
        Render-->>Sess: renderer reset
        deactivate Render
        Sess->>Buffer: new MasterAudioBuffer(sampleRate)
        activate Buffer
        Buffer-->>Sess: buffer
        deactivate Buffer
        Sess->>Analysis: new AnalysisWorker(buffer, config)
        activate Analysis
        Analysis-->>Sess: analysis worker
        deactivate Analysis
        Sess->>Analysis: Start()
        activate Analysis
        Analysis-->>Sess: analysis thread started
        deactivate Analysis
        Sess-->>App: buffer + runSessionToken
        deactivate Sess
        App->>Backend: CreateWorker(buffer)
        activate Backend
        Backend-->>App: WindowsAudio 또는 LinuxAudio worker
        deactivate Backend
        App->>Sess: AttachInputWorker(worker, token)
        activate Sess
        Sess-->>App: DataReady handler attached
        deactivate Sess
        App->>Input: Start(device, sampleRate, gain)
        activate Input
        Input-->>App: capture thread started
        deactivate Input
        App-->>Ops: started = true
        deactivate App
        Ops-->>Cmd: started = true
        deactivate Ops

    else Playback mode
        Cmd->>Ops: StartPlaybackAsync()
        activate Ops
        Ops->>App: PlaybackStart()
        activate App
        App->>User: WAV 파일 선택 요청
        User-->>App: WAV 파일 선택
        App->>App: 이전 live audio 상태 저장<br/>Playback source/rate 적용
        activate App
        deactivate App
        App->>Sess: PrepareInputRun(sampleRate)
        activate Sess
        Sess->>Sess: 새 runSessionToken 발급
        activate Sess
        deactivate Sess
        Sess->>Buffer: new MasterAudioBuffer(sampleRate)
        activate Buffer
        Buffer-->>Sess: buffer
        deactivate Buffer
        Sess->>Analysis: new AnalysisWorker(buffer, config)
        activate Analysis
        Analysis-->>Sess: analysis worker
        deactivate Analysis
        Sess->>Analysis: Start()
        activate Analysis
        Analysis-->>Sess: analysis thread started
        deactivate Analysis
        Sess-->>App: buffer + runSessionToken
        deactivate Sess
        App->>Input: new PlaybackWorker(buffer, rate)
        activate Input
        Input-->>App: playback worker
        deactivate Input
        App->>Sess: AttachInputWorker(worker, token)
        activate Sess
        Sess-->>App: DataReady handler attached
        deactivate Sess
        App->>Input: Start(filePath)
        activate Input
        Input-->>App: playback thread started
        deactivate Input
        App-->>Ops: started = true
        deactivate App
        Ops-->>Cmd: started = true
        deactivate Ops

    else Simulation mode
        Cmd->>Ops: StartSimulationAsync()
        activate Ops
        Ops->>App: SimStart()
        activate App
        App->>App: WatchSynthStreamConfig 구성
        activate App
        deactivate App

        opt 사용자가 녹음 저장을 선택한 경우
            App->>Writer: RecordingSessionService.TryStartAsync()
            activate Writer
            Writer-->>App: ISampleWriter
            deactivate Writer
        end

        App->>App: 이전 live audio 상태 저장<br/>Simulation source/rate 적용
        activate App
        deactivate App
        App->>Sess: PrepareInputRun(sampleRate)
        activate Sess
        Sess->>Sess: 새 runSessionToken 발급
        activate Sess
        deactivate Sess
        Sess->>Buffer: new MasterAudioBuffer(sampleRate)
        activate Buffer
        Buffer-->>Sess: buffer
        deactivate Buffer
        Sess->>Analysis: new AnalysisWorker(buffer, config)
        activate Analysis
        Analysis-->>Sess: analysis worker
        deactivate Analysis
        Sess->>Analysis: Start()
        activate Analysis
        Analysis-->>Sess: analysis thread started
        deactivate Analysis
        Sess-->>App: buffer + runSessionToken
        deactivate Sess
        App->>Input: new SimWorker(buffer, rate)
        activate Input
        Input-->>App: sim worker
        deactivate Input
        App->>Sess: AttachInputWorker(worker, token)
        activate Sess
        Sess-->>App: DataReady handler attached
        deactivate Sess
        App->>Input: Start(config)
        activate Input
        Input-->>App: sim thread started
        deactivate Input
        App-->>Ops: started = true
        deactivate App
        Ops-->>Cmd: started = true
        deactivate Ops
    end

    Cmd->>VM: RunState = Running<br/>Status = "Running"
    activate VM
    VM-->>Cmd: 상태 갱신 완료
    deactivate VM
    Cmd-->>App: StartAsync 완료
    deactivate Cmd
    App-->>VM: command 완료
    deactivate App
    deactivate VM

    loop 측정 중: 입력 block마다
        activate Input
        Input->>Buffer: WriteSamples(block)
        activate Buffer
        Buffer-->>Input: samples stored
        deactivate Buffer
        Input-->>Sess: DataReady
        activate Sess
        Sess->>Sess: runSessionToken 확인
        activate Sess
        deactivate Sess
        Sess->>Analysis: NotifyDataReady()
        activate Analysis
        Analysis-->>Sess: wakeup signaled
        deactivate Analysis
        Sess-->>Input: callback accepted
        deactivate Sess
        deactivate Input

        activate Analysis
        Analysis->>Buffer: CopyAnalysisSamples()
        activate Buffer
        Buffer-->>Analysis: analysis block
        deactivate Buffer
        opt 녹음 writer가 있을 때
            Analysis->>Writer: Write(block)
            activate Writer
            Writer-->>Analysis: queued
            deactivate Writer
        end
        Analysis->>Core: Process(block)
        activate Core
        Core-->>Analysis: Detection / metrics / projected frame data
        deactivate Core
        Analysis-->>App: AnalysisFrameReady(frame)
        activate App
        App->>Render: schedule latest frame
        activate Render
        Render->>VM: status/readout 갱신
        activate VM
        VM-->>Render: 상태 갱신 완료
        deactivate VM
        Render->>User: 활성 탭 렌더링
        Render-->>App: render queued/applied
        deactivate Render
        App-->>Analysis: frame observed
        deactivate App
        deactivate Analysis
    end

    alt 사용자가 측정 종료를 요청한 경우
        User->>VM: Pause 후 Reset 또는 내부 stop 요청
        activate VM
        VM->>App: TogglePause/Reset command
        activate App
        App->>Cmd: Reset() 또는 StopRunWithoutReset()
        activate Cmd
        Cmd->>VM: RunState = Stopping
        activate VM
        VM-->>Cmd: 상태 갱신 완료
        deactivate VM
        Cmd->>Ops: StopMode(CurrentMode)
        activate Ops

        alt Live stop
            Ops->>Sess: StopInputWorker("Audio")
            activate Sess
            Sess->>Input: TryStop(timeout)
            activate Input
            Input-->>Sess: stopped
            deactivate Input
            Sess-->>Ops: input stopped
            deactivate Sess
        else Playback stop
            Ops->>Sess: StopInputWorker("Playback")
            activate Sess
            Sess->>Input: TryStop(timeout)
            activate Input
            Input-->>Sess: stopped
            deactivate Input
            Sess-->>Ops: input stopped
            deactivate Sess
        else Simulation stop
            Ops->>Sess: StopInputWorker("Sim")
            activate Sess
            Sess->>Input: TryStop(timeout)
            activate Input
            Input-->>Sess: stopped
            deactivate Input
            Sess-->>Ops: input stopped
            deactivate Sess
        end

        Ops->>Sess: StopAnalysisThread()
        activate Sess
        Sess->>Analysis: TryStop(timeout)
        activate Analysis
        Analysis-->>Sess: stopped
        deactivate Analysis
        Sess-->>Ops: analysis stopped
        deactivate Sess
        Ops-->>Cmd: stop outcome
        deactivate Ops

        opt 녹음 writer가 있을 때
            Cmd->>Ops: CloseAudio()
            activate Ops
            Ops->>Writer: CloseAudio()
            activate Writer
            Writer-->>Ops: closed
            deactivate Writer
            Ops-->>Cmd: closed
            deactivate Ops
        end

        Cmd->>Ops: InvalidateRunSession()
        activate Ops
        Ops->>Sess: InvalidateRunSession()
        activate Sess
        Sess-->>Ops: token advanced
        deactivate Sess
        Ops-->>Cmd: invalidated
        deactivate Ops

        opt Playback 또는 Simulation이면
            Cmd->>Ops: RestorePlaybackOrSimulationAudioState()
            activate Ops
            Ops->>App: RestorePlaybackOrSimulationAudioState()
            activate App
            App-->>Ops: restored
            deactivate App
            Ops-->>Cmd: restored
            deactivate Ops
        end

        Cmd->>VM: RunState = Stopped<br/>Status = "Stopped" 또는 "Reset"
        activate VM
        VM-->>Cmd: 상태 갱신 완료
        deactivate VM
        Cmd-->>App: stop/reset 완료
        deactivate Cmd
        App-->>VM: command 완료
        deactivate App
        deactivate VM

    else Playback/Simulation 입력이 자연 종료된 경우
        Input-->>App: DoneReadingFile / SimDone
        activate App
        App->>Sess: IsCurrentRunSession(token)
        activate Sess
        Sess-->>App: current
        deactivate Sess
        App->>Sess: InvalidateRunSession()
        activate Sess
        Sess-->>App: token advanced
        deactivate Sess
        App->>VM: RunState = Stopping
        activate VM
        VM-->>App: 상태 갱신 완료
        deactivate VM
        App->>Sess: StopInputWorker(...)
        activate Sess
        Sess->>Input: TryStop(timeout)
        activate Input
        Input-->>Sess: stopped
        deactivate Input
        Sess-->>App: input stopped
        deactivate Sess
        App->>Sess: StopAnalysisThread(completeInput: true)
        activate Sess
        Sess->>Analysis: CompleteInput(timeout)
        activate Analysis
        Analysis->>Core: DrainAndFlushInput()
        activate Core
        Core-->>Analysis: final projected data
        deactivate Core
        Analysis-->>App: final AnalysisFrameReady(frame)
        activate App
        App-->>Analysis: final frame accepted
        deactivate App
        Analysis-->>Sess: completed
        deactivate Analysis
        Sess-->>App: analysis completed
        deactivate Sess
        opt 녹음 writer가 있을 때
            App->>Writer: AudioCloseCheck()
            activate Writer
            Writer-->>App: closed
            deactivate Writer
        end
        App->>App: RestorePlaybackOrSimulationAudioState()
        activate App
        deactivate App
        App->>VM: RunState = Stopped<br/>Status = "Stopped"
        activate VM
        VM-->>App: 상태 갱신 완료
        deactivate VM
        deactivate App

    else Live capture가 예기치 않게 종료된 경우
        Input-->>App: CaptureEnded
        activate App
        App->>Sess: IsCurrentRunSession(token)
        activate Sess
        Sess-->>App: current
        deactivate Sess
        App->>Cmd: StopRunAndRefreshDevices()
        activate Cmd
        Cmd->>Ops: StopLive()
        activate Ops
        Ops->>Sess: StopInputWorker("Audio")
        activate Sess
        Sess->>Input: TryStop(timeout)
        activate Input
        Input-->>Sess: stopped
        deactivate Input
        Sess-->>Ops: input stopped
        deactivate Sess
        Ops->>Sess: StopAnalysisThread()
        activate Sess
        Sess->>Analysis: TryStop(timeout)
        activate Analysis
        Analysis-->>Sess: stopped
        deactivate Analysis
        Sess-->>Ops: analysis stopped
        deactivate Sess
        Ops-->>Cmd: stopped
        deactivate Ops
        Cmd->>Ops: RefreshDevices()
        activate Ops
        Ops->>App: LoadAudioDevices()
        activate App
        App-->>Ops: devices refreshed
        deactivate App
        Ops-->>Cmd: refreshed
        deactivate Ops
        Cmd->>VM: RunState = Stopped<br/>Status = "Live audio capture ended unexpectedly"
        activate VM
        VM-->>Cmd: 상태 갱신 완료
        deactivate VM
        Cmd-->>App: recovery stop 완료
        deactivate Cmd
        deactivate App
    end

    User->>App: 프로그램 종료
    activate App
    App->>App: OnWindowClosed()
    activate App
    deactivate App
    App->>Sess: InvalidateRunSession()
    activate Sess
    Sess-->>App: token advanced
    deactivate Sess
    App->>Sess: StopInputWorker("Input")
    activate Sess
    Sess->>Input: TryStop(timeout)
    activate Input
    Input-->>Sess: stopped
    deactivate Input
    Sess-->>App: input stopped
    deactivate Sess
    App->>Sess: StopAnalysisThread()
    activate Sess
    Sess->>Analysis: TryStop(timeout)
    activate Analysis
    Analysis-->>Sess: stopped
    deactivate Analysis
    Sess-->>App: analysis stopped
    deactivate Sess
    opt 녹음 writer가 있을 때
        App->>Writer: AudioCloseCheck() / Dispose()
        activate Writer
        Writer-->>App: closed/disposed
        deactivate Writer
    end
    App-->>User: 프로세스 종료
    deactivate App
```

## 표기 기준

| 표기 | 의미 |
|---|---|
| `alt` | 하나의 실행 trace 안에서 조건에 따라 갈라지는 입력 모드 또는 종료 경로 |
| `opt` | 녹음 저장처럼 선택되었을 때만 실행되는 조건부 상호작용 |
| `loop` | 입력 worker가 오디오 block을 쓰고 분석 worker가 frame을 만드는 반복 흐름 |
| `activate` / `deactivate` | 호출을 수행하는 동안의 focus of control 세로 막대. Mermaid 기본 테마 변수로 전체 막대 색만 통일한다 |
| `RunSessionController`의 token 확인 | 오래된 입력 콜백이 새 실행 세션을 깨뜨리지 않게 하는 timestamp tactic |

## 근거 모듈

| 책임 | 코드 위치 |
|---|---|
| 실행 상태 전이 | `src/TimeGrapher.App/Services/RunCommandService.cs`, `RunCommandService.States.cs` |
| 세션 token, worker attach/stop, 분석 worker 시작 | `src/TimeGrapher.App/Services/RunSessionController.cs` |
| Live/Playback/Simulation 시작과 종료 wiring | `src/TimeGrapher.App/Views/MainWindow.RunLifecycle.cs`, `MainWindow.RunCommandOperations.cs` |
| Live backend 선택 | `src/TimeGrapher.App/Audio/LiveAudioBackend.cs` |
| 공통 입력 worker 계약 | `src/TimeGrapher.Core/Shared/IAudioInputWorker.cs`, `ILiveAudioWorker.cs` |
| 오디오 ring buffer와 분석 처리 | `src/TimeGrapher.Core/Shared/MasterAudioBuffer.cs`, `src/TimeGrapher.Core/Analysis/AnalysisWorker.cs` |

## 아키텍처 해석

- Layered View 기준으로 App은 UI/controller 계층에서 Core와 플랫폼 어댑터를 사용하고, Core는 App/UI/플랫폼을 알지 않는다.
- MVC View 기준으로 사용자 입력은 View/Controller(`MainWindow`, `RunCommandService`)에서 처리되고, 분석 데이터는 Model 역할의 Core와 `MainWindowViewModel`을 거쳐 View/Renderer에 표시된다.
- SAP tactics 기준으로 실행 세션 token은 `timestamp`, 입력/분석/렌더 분리는 `introduce concurrency`, 최신 frame만 렌더링하는 경로는 `limit event response`, 실행 상태 객체는 State Pattern에 해당한다.
