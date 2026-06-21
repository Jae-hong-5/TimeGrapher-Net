# 실행 수명주기 시퀀스 뷰

| 페이지 | 내용 |
| --- | --- |
| Level 1 | 실행 수명주기 개요 |
| Level 2.1 | 입력 실행 준비 공통 흐름 |
| Level 2.2 | 입력 모드별 시작 흐름 |
| Level 2.3 | 측정 중 분석 반복 흐름 |
| Level 2.4 | 측정 종료 (사용자 요청 / Live capture 종료) |
| Level 2.5 | Playback/Simulation 자연 종료 |
| Level 2.6 | 프로그램 종료 teardown |

## Level 1 · 실행 수명주기 개요

실행 → 시작 → 측정 → 종료 골격만 보여주고, 세부 흐름은 `ref [Level 2.x]`로 가린다.

![Level 1 실행 수명주기 개요](assets/uml25/run-lifecycle-seq-level1.svg)

## Level 2 · 세부 시퀀스

### Level 2.1 · 입력 실행 준비 공통 흐름

세 입력 모드가 공통으로 거치는 분석 세션 준비: `PrepareInputRun`, `runSessionToken` 발급, `MasterAudioBuffer` 생성, `AnalysisWorker` 생성/시작.

![Level 2.1 입력 실행 준비 공통 흐름](assets/uml25/run-lifecycle-seq-level21.svg)

### Level 2.2 · 입력 모드별 시작 흐름

Live / Playback / Simulation의 소스 준비와 worker 생성 차이만 `alt`로 표시한다.

![Level 2.2 입력 모드별 시작 흐름](assets/uml25/run-lifecycle-seq-level22.svg)

### Level 2.3 · 측정 중 분석 반복 흐름

입력 block마다 `Input → Buffer → AnalysisWorker → Core → App UI 갱신`이 반복된다.

![Level 2.3 측정 중 분석 반복 흐름](assets/uml25/run-lifecycle-seq-level23.svg)

### Level 2.4 · 측정 종료 (사용자 요청 / Live capture 종료)

사용자의 Reset/내부 stop 요청, 또는 Live capture가 스스로 끝난 경우(`CaptureEnded` → `StopRunAndRefreshDevices`)가 모두 같은 즉시 정지 시퀀스로 수렴한다. 입력·분석 worker를 `TryStop`으로 멈추고 세션을 무효화하며, Playback/Simulation이면 오디오 상태를 복원한 뒤 `RunState = Stopped`로 전이한다.

![Level 2.4 측정 종료 (사용자 요청 / Live capture 종료)](assets/uml25/run-lifecycle-seq-level24.svg)

### Level 2.5 · Playback/Simulation 자연 종료

입력이 끝나면(`DoneReadingFile` / `SimDone`) 현재 세션을 확인·무효화하고 `RunState = Stopping`으로 전이한 뒤 오디오 상태를 복원한다. 이어서 `CompleteInput` → `DrainAndFlushInput`으로 마지막 frame까지 반영하고 정리한다.

![Level 2.5 Playback/Simulation 자연 종료](assets/uml25/run-lifecycle-seq-level25.svg)

### Level 2.6 · 프로그램 종료 teardown

창이 닫히면(`OnWindowClosed`) 현재 세션을 먼저 무효화한 뒤 입력·분석 worker를 정지하고 프로세스를 종료한다.

![Level 2.6 프로그램 종료 teardown](assets/uml25/run-lifecycle-seq-level26.svg)
