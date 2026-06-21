# 실행 수명주기 시퀀스 뷰 (MVVM)

측정 실행(run)의 시작 → 측정 → 종료 동안 객체들이 "누가 누구를 호출하나"를 보여주는 동작(behavior) 뷰다. MVC → MVVM 리팩토링 이후, 과거 하나의 거대 컨트롤러(`MainWindow`)에 뭉쳐 있던 UI 상태·명령·실행 오케스트레이션을 **View / ViewModel / RunCommandService** 세 협력자로 분리한 구조를 반영한다.

> 이 문서는 아키텍처 뷰 템플릿(주 표현 · 요소 카탈로그 · 동작 · 표기 · 가변성 · 설계 근거 · 관련 뷰)을 따른다. 반복 루프가 있어 가장 세분화가 필요한 **측정 흐름만 Level 2 자식 뷰**로 분리하고, 시작·종료·프로그램 종료는 핵심 호출만 남겨 **Level 1 개요에 간소화해 통합**했다.

## 문서 로드맵

| 페이지 | 내용 |
| --- | --- |
| Level 1 | 실행 수명주기 개요 — 시작 · 종료 · 프로그램 종료를 간소화 통합 |
| Level 2 | 측정 중 분석 반복 흐름 (Level 1의 `ref`를 펼친 자식 뷰) |

## 1. 주 표현 (Primary Presentation) · Level 1 · 실행 수명주기 개요

한 장에 수명주기 전체를 담는다. 사용자 입력이 **View → ViewModel 명령(Command 바인딩) → RunCommandService** 로 흐르는 MVVM 경로를 드러내며, 다음을 간소화해 인라인한다.

- **시작**: `StartMode` → `PrepareInputRun`(`MasterAudioBuffer`·`AnalysisWorker` 생성) → 입력 worker 생성/시작. 세 입력 모드(Live/Playback/Simulation)의 차이는 한 메시지로 압축한다.
- **측정**: 반복 루프만 `ref [Level 2]`로 가린다.
- **종료**: 세 트리거(사용자 Reset / 외부 capture 끊김 / Playback 자연 종료)를 `alt`로 묶고, 같은 View 주도 worker 정지로 수렴한다.
- **프로그램 종료**: 창 닫힘 → `OnWindowClosed` → 프로세스 종료.

실행 제어 상태(`RunState`)의 전이는 이 뷰에서 표현하지 않고 [상태 머신 뷰](RUN_LIFECYCLE_STATE_MACHINE.md)에서 다룬다.

![Level 1 실행 수명주기 개요](assets/uml25/run-lifecycle-seq-level1.svg)

## 2. 요소 카탈로그 (Element Catalog)

각 lifeline의 MVVM 역할과 근거 코드. `Core`는 어디에도 의존하지 않으며, View → ViewModel/Service, Service → View(인터페이스 경유)의 의존만 존재한다. `MasterAudioBuffer`·`Core pipeline`은 Level 2에서만 lifeline으로 등장한다.

| Lifeline | MVVM 레이어 | 책임 | 코드 위치 |
| --- | --- | --- | --- |
| User | (actor) | 사용자 | — |
| View (`MainWindow`) | View | UI 이벤트 수신, 렌더링·스레드 마샬링, `RunSessionController`로 입력·분석 worker 수명 구동, 서비스의 `IRunCommandOperations` 콜백 구현 | `src/TimeGrapher.App/Views/MainWindow*.cs` |
| ViewModel (`MainWindowViewModel`) | ViewModel | `PlayPauseCommand`/`ResetCommand` 노출, 관찰 가능한 `RunState`/`StatusText`. 도메인을 직접 호출하지 않음 | `src/TimeGrapher.App/ViewModels/MainWindowViewModel.cs` |
| RunCommandService | App 서비스 (State Pattern) | 시작/일시정지/정지 오케스트레이션. ViewModel 상태를 갱신하고 `IRunCommandOperations`로 View를 호출 | `src/TimeGrapher.App/Services/RunCommandService*.cs` |
| RunSessionController | Model 경계 | 실행 세션 token, 입력 worker attach/stop, 분석 worker 수명 | `src/TimeGrapher.App/Services/RunSessionController.cs` |
| Input worker | Model | Live=`AudioCaptureWorker`, Playback=`PlaybackWorker`, Simulation=`SimWorker` | `App.Audio` / `Core.AudioIo` / `Core.Sim` |
| MasterAudioBuffer | Model | 입력↔분석 공유 오디오 ring buffer | `TimeGrapher.Core` |
| AnalysisWorker | Model | 분석 스레드 | `TimeGrapher.Core.Analysis` |
| Core pipeline | Model | Detection / Metrics / Projectors | `TimeGrapher.Core` |

명령 본문은 ViewModel이 보유하고, 실제 실행 동작은 주입된 `IRunCommandRunner`(=`RunCommandService`)가 수행한다. 서비스가 View를 호출할 때는 `IRunCommandOperations` 인터페이스를 거쳐 의존을 단방향(서비스 → 인터페이스 ← View)으로 유지한다.

## 3. 동작 (Behavior) · Level 2 · 측정 중 분석 반복 흐름

Level 1의 측정 `ref`를 펼친 **자식 뷰**다. 입력 block마다 `Input → Buffer → AnalysisWorker → Core`가 돌고(처음에 입력 worker가 block을 생성하는 동안의 활성 구간을 막대로 표시), 분석 frame은 **View**가 받아 그래프를 렌더한 뒤 **ViewModel**(`Present()`)로 `Status`·`AwaitingBeatSync`·`Review`를 갱신한다. 이 루프는 Live·Simulation에서는 정지 전까지 무한 반복되고, Playback에서는 WAV EOF에서 자연 종료한다(→ Level 1의 종료 `alt`).

![Level 2 측정 중 분석 반복 흐름](assets/uml25/run-lifecycle-seq-level2.svg)

## 4. 표기 (Notation)

표기는 **UML 2.5 시퀀스 다이어그램** 표준을 따른다(lifeline, 실행 occurrence 막대, 동기 호출/응답 메시지, `alt`/`opt`/`loop` 조합 fragment, `ref` 상호작용 사용). 라벨 규칙만 짧게: User↔시스템 화살표는 사용자의 의도/행위, 객체 간 화살표는 오퍼레이션 시그니처(코드 추적성)다. **실행 제어 상태(`RunState`)의 정의·전이는 이 뷰에서 표현하지 않고 [상태 머신 다이어그램](RUN_LIFECYCLE_STATE_MACHINE.md)에서 다룬다 — 이 시퀀스 뷰는 객체 상호작용에 집중한다.** Level 1의 시작·종료·프로그램 종료는 핵심 호출만 남기고 ack/return을 간소화했다.

## 5. 가변성 (Variability)

입력 소스(Live / Playback / Simulation)가 유일한 변이점이며, 별도 가변성 메커니즘 없이 런타임에 `CurrentMode` 분기로 처리된다(Level 1 시작 메시지에 압축, 종료 `alt`에 반영).

## 6. 설계 근거 (Design Rationale)

- **결정**: MVC의 단일 거대 컨트롤러(`MainWindow`)에 섞여 있던 UI 상태·명령·실행 오케스트레이션을 MVVM 세 역할로 분리했다 — View(렌더링·플랫폼·세션 배선), ViewModel(바인딩 가능한 상태/명령), RunCommandService(실행 상태기계, State Pattern).
- **근거**: 관심사 분리로 수정용이성·시험용이성을 높인다. ViewModel은 도메인을 직접 호출하지 않아 윈도 없이 단위 테스트가 가능하고(`RunState`/명령 활성화 로직), 서비스↔View 결합은 `IRunCommandRunner`(명령 본문 주입)와 `IRunCommandOperations`(서비스→View 콜백) 인터페이스로 역전해 의존을 단방향으로 유지한다.
- **기각한 대안**: View가 명령 본문을 `Func`/`Action` 델리게이트로 ViewModel에 주입하던 MVC 잔재 방식. 명령이 자기 본문을 ViewModel 안에 두도록 `IRunCommandRunner`를 주입하는 방식으로 대체했다.
- **의도된 예외**: Playback 자연 종료·프로그램 종료는 worker 완료/창 닫힘 콜백을 받는 View가 직접 처리하여 `RunCommandService`를 우회한다(Level 1 종료 `alt`의 "View 직접 처리" 분기). 비-사용자 트리거 완료 경로다.

## 7. 관련 뷰 (Related Views)

- [상태 머신 다이어그램](RUN_LIFECYCLE_STATE_MACHINE.md) — 같은 실행 수명주기를 제어 상태(`RunUiState` + State Pattern)의 전이로 본 자매 뷰.
- [원본 실행 수명주기 시퀀스 뷰](../RUN_LIFECYCLE_SEQUENCE_VIEW.md) — 레벨링 이전 단일 시퀀스(아직 MVVM 분리를 반영하지 않을 수 있음).
- 편집 원본: [sequence.drawio](sequence.drawio) — draw.io로 직접 편집하는 단일 소스. SVG 갱신은 `python _drawio_to_svg.py`([_drawio_to_svg.py](_drawio_to_svg.py))로 한다(draw.io 설치 불필요). 초기 골격 생성기 [_gen_sequence.py](_gen_sequence.py)는 보관용이며, 다시 실행하면 수동 편집한 drawio를 덮어쓴다.
