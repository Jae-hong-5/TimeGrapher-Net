# Milestone Architecture Review Notes

이 문서는 현재 `TimeGrapher-Docs/Milestone/en`의 architecture 자료를 강의 피드백과
현재 source architecture 관점에서 검토한 메모다. 구현 브랜치에 판단 근거와 발표 보완
방향을 남기기 위해 source repo의 `docs/architecture/` 아래에 둔다.

## 검토한 자료

- `D:\swa\TimeGrapher-Docs\Milestone\en\5-Architectural-View.md`
- `D:\swa\TimeGrapher-Docs\Milestone\en\Milestone2 Review Q&A.md`
- `D:\swa\TimeGrapher-Docs\Milestone\en\2-Architectural-Drivers.md`, 특히 QAS-5 modifiability
- `D:\swa\TimeGrapher-Docs\Milestone\en\3-Risk-Assessment.md`, 특히 R-17 TinyML 및 extension risk
- `D:\swa\TimeGrapher-Docs\Milestone\en\4-Planned-Experiments.md`, 특히 EXP-03 및 EXP-04

## 잘한 점

- Architectural view에 강의에서 요구하는 주요 view type이 들어 있다: MVVM/module uses,
  runtime sequence/state behavior, deployment, related layered view.
- One-way dependency rule이 명확하고 실제 source 구조와도 추적 가능하다. App과 platform
  adapter는 Core에 의존하고, Core는 UI/platform에 의존하지 않는다.
- Runtime behavior가 그림만 있는 것이 아니라 `RunCommandService`, `RunSessionController`,
  input worker, `AnalysisWorker`, shared buffer, UI rendering 같은 구체적인 mechanism으로
  설명되어 있다.
- Q&A가 architecture decision을 quality attribute와 연결한다. QAS-2는 real-time
  performance, QAS-4는 consistency, QAS-5는 modifiability, QAS-6은 touchscreen usability와
  연결되어 있다.
- TinyML decision을 확정된 scope처럼 쓰지 않고 unresolved risk로 다룬 점이 좋다. R-17과
  EXP-04는 adoption을 latency와 trustworthiness evidence에 조건부로 묶고 있다.

## 발표 전 보완할 점

1. **각 view의 목적 설명이 아직 암시적이다.** 문서는 view를 나열하지만, 각 view가 어떤
   stakeholder question에 답하는지 더 직접적으로 말해야 한다. 예를 들어 module uses view는
   "무엇이 무엇에 의존할 수 있는가?"에 답하고, runtime C&C view는 "audio block 하나가
   UI blocking 없이 visible output이 되는 과정은 무엇인가?"에 답하며, deployment view는
   "OS/hardware dependency가 어디에서 들어오는가?"에 답한다.

2. **View 간 관계를 연결하는 bridge가 필요하다.** 현재 문서는 view를 순서대로 제시하지만
   서로 어떻게 연결되는지는 약하다. Layered view가 allowed dependency rule을 정의하고,
   module uses view가 그 rule의 code-level realization을 보여주며, MVVM view가 App 내부
   responsibility separation을 확대해서 보여주고, sequence/state view가 같은 static element가
   runtime에 어떻게 협력하는지 보여준다는 짧은 mapping을 추가하는 것이 좋다.

3. **QAS-5 evidence는 실제 변경 사례로 보여주는 것이 좋다.** Architectural drivers는
   "new graph/filter/measurement는 existing module <= 1개만 건드린다"고 정의한다. 발표에서는
   이를 현재 feature 하나로 보여줘야 한다. Signal-quality overlay propagation이 좋은 예다.
   Core는 `SignalQualityFlags`를 내보내고, App은 Beat Noise/Waveform Compare/Escapement
   rendering class에서 이를 표시한다. Platform adapter나 Core dependency direction은 바뀌지 않는다.

4. **TinyML extensibility의 insertion point를 더 분명히 해야 한다.** 문서는 TinyML과
   rule-based fallback을 언급하지만, 발표에서는 의도한 module boundary를 더 명확히 말해야 한다.
   향후 inference module은 Core 밖에 두고 기존 Core contract/gate socket을 사용해야 하며,
   Core가 UI/platform/model runtime package에 직접 의존하게 만들면 안 된다.

5. **Error/warning UX를 reliability와 연결해야 한다.** R-07과 QAS-3은 weak/noisy signal
   handling을 다룬다. 발표에서는 GUI가 misleading reading을 조용히 보여주지 않도록 어떻게 막는지
   보여줘야 한다. Warning은 readout/status guidance/diagnostic graph overlay로 전달되고,
   suspicious C candidate는 amplitude update에서 제외된다.

## 추천 발표 문서 수정안

`5-Architectural-View.md` 앞부분에 작은 "View Purpose and Traceability" 표를 추가하는 것이 좋다.

| View | 답하는 질문 | 연결되는 quality attribute |
|---|---|---|
| Layered View | 어떤 dependency가 허용되는가? | QAS-5 modifiability, portability |
| Module Uses View | 실제 code는 무엇에 의존하는가? | QAS-5, C-3 cross-platform |
| MVVM View | UI responsibility는 어떻게 분리되는가? | QAS-5 testability/modifiability |
| Sequence/C&C View | Run 중 data는 어떻게 이동하는가? | QAS-2 latency, QAS-4 consistency |
| State Machine View | 어떤 user/run state가 합법적인가? | Reliability and usability |
| Deployment View | Hardware와 OS boundary는 어디에서 들어오는가? | C-3, QAS-2 |

"How the views connect" paragraph도 추가하는 것이 좋다.

> Layered view는 dependency rule을 정한다. Module uses view는 구현이 그 rule을 따르는지
> 검증한다. MVVM view는 App layer 내부를 확대하여 UI responsibility separation을 보여준다.
> Sequence와 state-machine view는 같은 module들이 measurement run 중에 어떻게 협력하는지
> 보여준다. Deployment view는 동일한 runtime path를 Windows/Raspberry Pi 및 microphone
> hardware boundary 위에 배치한다.

Modifiability의 concrete example도 추가하는 것이 좋다.

> 예: signal-quality propagation은 platform adapter를 변경하거나 Core dependency를 역전시키지
> 않고 user-visible warning을 추가했다. Core는 shared DTO로 quality flag를 publish하고,
> App rendering consumer가 관련 graph에 warning을 표시한다. 변경이 shared contract,
> analysis/metrics logic, 관련 rendering consumer로 제한되므로 QAS-5 extension rule을 따른다.

## Source Architecture 관점의 결론

현재 구현은 문서화된 architecture와 일관된다.

- Core는 signal interpretation과 DTO contract를 계속 담당한다.
- App은 presentation state, warning text, overlay fade behavior, recovery guidance를 담당한다.
- Acoustic quality classification은 OS capture API와 독립적이므로 platform adapter는 변경하지 않는다.
- 향후 TinyML은 optional inference/gate boundary로 들어와야 하며, Core가 UI/platform/model runtime
  package에 의존하게 만들면 안 된다.
