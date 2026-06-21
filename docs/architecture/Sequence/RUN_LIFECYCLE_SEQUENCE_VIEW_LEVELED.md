# 실행 수명주기 시퀀스 뷰

| 페이지 | 내용 |
| --- | --- |
| Level 1 | 실행 수명주기 개요 |
| Level 2.1 | 입력 실행 준비 공통 흐름 |
| Level 2.2 | 입력 모드별 시작 흐름 |
| Level 2.3 | 측정 중 분석 반복 흐름 |
| Level 2.4 | 종료 및 정리 흐름 |

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

### Level 2.4 · 종료 및 정리 흐름

사용자 요청 종료, Playback/Simulation 자연 종료, 프로그램 종료(창 닫기)를 하나의 `alt`로 비교한다.

![Level 2.4 종료 및 정리 흐름](assets/uml25/run-lifecycle-seq-level24.svg)
