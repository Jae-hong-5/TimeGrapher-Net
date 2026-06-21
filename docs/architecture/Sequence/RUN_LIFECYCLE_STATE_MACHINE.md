# 실행 수명주기 상태 머신

`RunCommandService`의 실행 제어 상태(State Pattern)와 `RunUiState`를 상태 머신으로 본 뷰다. "누가 누구를 호출하나"(객체 상호작용)는 [시퀀스 뷰](RUN_LIFECYCLE_SEQUENCE_VIEW_LEVELED.md)에서, "실행 상태가 어떻게 바뀌나"(제어 상태)는 이 문서에서 다룬다.

![Run 상태 머신](assets/uml25/run-lifecycle-state.svg)

편집 원본: [state.drawio](state.drawio)

## 상태

| 상태 | 의미 |
| --- | --- |
| Stopped | 측정 중이 아님 (초기 상태) |
| Starting | 시작 절차 진행 중 |
| Running | 측정 중 |
| Paused | 일시정지 — worker는 살아 있고 입력만 gate |
| Stopping | 정지 절차 진행 중 |
| StopFailed | worker 정지 / 녹음 close 실패 — 재시도 대기 |

## 전이

| From → To | 트리거 / 조건 |
| --- | --- |
| Stopped → Starting | Start |
| Starting → Running | 시작 성공 |
| Starting → Stopped | 시작 실패 → 정리(`CleanupFailedStart`) |
| Running → Paused | Pause |
| Paused → Running | Resume |
| Running / Paused → Stopping | 사용자 Stop·Reset, 또는 Live capture·Playback 자연 종료 |
| Stopping → Stopped | 정지 성공 |
| Stopping → StopFailed | worker timeout 또는 녹음 close 실패 |
| StopFailed → Stopping / Stopped | Stop·Reset 재시도(`RetryPendingStop`) |

## 근거 모듈

| 책임 | 코드 위치 |
| --- | --- |
| 상태 정의 | `src/TimeGrapher.App/ViewModels/MainWindowViewModel.cs` (`RunUiState`) |
| State Pattern 상태 객체 | `src/TimeGrapher.App/Services/RunCommandService.States.cs` |
| 전이 메서드(`SetStarting`/`SetRunning`/`SetStopping`/`SetStopped`/`SetStopFailed`) | `src/TimeGrapher.App/Services/RunCommandService.cs` |

## 표기

- 채워진 원: 초기 의사상태(initial pseudostate).
- 둥근 사각형: 상태. 화살표: 전이(트리거/조건 라벨).

> SAP tactics 기준 "실행 상태 객체는 State Pattern"에 해당한다. 시퀀스 뷰에 흩어진 `RunState = X` 표기는 이 상태 머신의 상태를 가리키는 맥락용 주석이다.
