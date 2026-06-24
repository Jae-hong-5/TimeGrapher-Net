# TinyML A/C Landmark Refiner 구현 메모

> **상태 (2026-06-24): 이 landmark-refiner(relocate) 접근은 코드에서 제거되었습니다** (commit `0d2b22b`). 실험 결과 실제 문제에 안 맞았다 — worst-case B→A는 onset 트리거 *아래*의 약한 A(재배치할 metrics 윈도우에 그 정보가 없음)이고, 합성-only 모델은 real 샘플을 악화시켰다(sim→real gap, `LANDMARK_REFINER_BEAT_DIAGNOSIS.md`/`TimeGrapher-Refiner` repo). weak-A는 **결정론적 phase-guided onset rescue**로 해결했고(`PhaseGuideOnsetRescueScale` — 출시됨, App "Weak-A onset rescue" 토글), 가짜비트/false-lock 방향은 기존 `IBeatEventGate` 분류기 소켓의 몫이다(`TINYML_CANDIDATE_GATE_PLAN.md`). **이 문서는 그 탐색의 기록으로 보존**하며, 아래 "구현 순서/현황"은 *제거 전* 상태를 가리킨다.

## 목적

이 문서는 TinyML 기반 A/C landmark 보정 기능의 설계와 구현 현황을 정리한다. 아래 본문은 설계 의도(원안)와 구현 결과(정정 표시)를 함께 담는다.

기존 TinyML 계획은 `IBeatEventGate`를 통해 후보 이벤트를 통과/거부하는 drop-only 필터였다. 그 방향은 잡음 후보를 줄이는 데는 유효하지만, 이번에 해결하려는 문제와는 다르다.

이번 목표는 **A/B/C impact가 있는 beat packet에서 A 또는 C가 약할 때 B를 A 또는 C로 잘못 잡는 문제를 TinyML로 줄이는 것**이다.

## 문제 정의

시계 beat packet에는 보통 A/B/C 성분이 있다.

현재 detector는 packet 전체를 의미론적으로 분해하지 않는다.

- A: envelope가 onset threshold를 처음 넘는 burst start로 잡는다.
- C: burst start 이후 `CSearchSkipSamples`가 지난 뒤 가장 큰 peak로 잡는다.
- B: 별도 landmark로 모델링하지 않는다.

따라서 다음 실패가 가능하다.

- **B -> A 오인식**: A가 작아 threshold를 못 넘고 B가 먼저 threshold를 넘으면 B가 A로 기록된다.
- **B -> C 오인식**: A는 잡혔지만 B가 `CSearchSkipSamples` 이후에 있고 C보다 크면 B가 C로 기록된다.

이 문제는 단순 event veto로는 충분히 해결되지 않는다. 잘못 잡은 후보를 버릴 수는 있지만, 진짜 A/C 위치로 시간을 보정할 수 없기 때문이다.

### 검증 결과: A 보정을 우선한다

synthetic fixture 스윕(`tests/TimeGrapher.Core.Tests/BeatLandmarkRefinerSyntheticTests.cs`)에서 두 실패의 성격이 다르게 나타났다.

- **B -> A**: A 클러스터만 약화해도(B/C 정상) 발생한다. A는 onset threshold 교차로 잡히므로 약하면 못 넘고 B가 첫 교차가 되기 때문이다. 정상 A는 onset이 약 0.13 ms로 정확히 잡히지만, 약한 A(클러스터 스케일 0.3)는 B로 점프해 약 2.4 ms 오차가 난다. 극단적 조건이 필요 없는 **현실적** 실패이고, rate/beat error의 기준이 되는 A 타이밍을 오염시키므로 더 **심각**하다.
- **B -> C**: detector가 강건하다. C가 약하고(스케일 <= ~0.05) **동시에** B가 압도적(>= ~10x)일 때만 깨지며, 더 완만한 weak-C는 C를 정확히 잡는다. 흔한 실패가 아니라 극단적 조건에서만 나타나는 caricature다.

따라서 우선순위는 다음과 같다.

- 모델/보정 설계와 학습 데이터는 **A 보정을 C 보정보다 우선**한다.
- synthetic 학습 데이터는 weak-A(B -> A) 케이스를 더 비중 있게 만든다.
- B -> C는 부차적 caricature로 두고, C 보정 경로 검증 용도로만 쓴다.

real sample 진단에서도 B->A가 확인되었다(`LANDMARK_REFINER_BEAT_DIAGNOSIS.md`): clean 레퍼런스(ST3600/NH39A)는 A 위상 이상치가 0인데, NH35는 드물게(1비트), `mine.wav`는 간헐적으로(~1.5%), 약신호 `mine_adapter.wav`는 만연하게(비트의 17%가 A>+2 ms 늦음, 42%가 진폭 dip) 나타난다 — 빈도가 신호 약화에 비례한다. 즉 합성 추정이 아니라 실측으로 A 우선이 뒷받침된다.

단, refiner는 metrics/display 스트림만 보정한다. 보정된 A는 rate/beat error 측정을 개선하지만, PLL/BPH lock 입력은 raw 스트림을 유지하므로 lock 자체는 보정하지 않는다(설계상 경계, "기존 C-onset timing과의 경계" 절 참조).

## 결론

TinyML 적용 방향은 `IBeatEventGate` 기반 drop-only 필터가 아니라 **TinyML Beat Landmark Refiner**로 잡는다.

```text
raw audio/envelope
-> existing detector raw A/C candidates
-> post-lock beat window capture
-> TinyML landmark refiner
-> corrected A/C samples for metrics/display
```

기존 detector는 유지한다. TinyML은 detector를 대체하지 않고, post-lock 상태에서 metrics/display에 들어갈 A/C timestamp만 제한적으로 보정한다.

## 기존 gate 한계

현재 준비된 TinyML seam은 `src/TimeGrapher.Core/Detection/Scoring/IBeatEventGate.cs`이다.

이 interface는 다음 특성을 가진다.

- 후보 event를 drop할 수 있다.
- 새 event를 만들 수 없다.
- event 시간을 수정할 수 없다.
- BPH detection과 sync PLL은 raw event stream을 그대로 본다.

이 구조는 잡음 event veto에는 안전하지만, B를 A/C로 잡은 경우의 timing correction에는 맞지 않는다.

따라서 새 seam이 필요하다.

## 새 seam 제안

Core에는 ML runtime을 넣지 않는다. Core에는 dependency-free contract만 둔다.

구현된 interface (`IBeatLandmarkRefiner`, `be5ca5f` — 아래 시그니처대로 구현됨):

```csharp
public interface IBeatLandmarkRefiner
{
    string Name { get; }
    double WindowPreMs { get; }
    double WindowPostMs { get; }

    BeatLandmarkRefinement Refine(
        ReadOnlySpan<float> envelopeWindow,
        int aOffsetInWindow,
        int cOffsetInWindow,
        double sampleRate,
        in BeatLandmarkCandidate candidate);

    void Reset();
}
```

구현된 result (`BeatLandmarkRefinement`, `be5ca5f`): 초기 제안의 `BAsARisk`/`BAsCRisk` 필드는 런타임 refinement에서 **제거**하고 학습 데이터 라벨로 옮겼다(아래 "모델 출력" 참조). C를 주 보정 대상으로 두어 C 필드를 앞에 두고, A 보정은 optional 기본값으로 둔다.

```csharp
public readonly record struct BeatLandmarkRefinement(
    bool Accepted,
    bool CorrectedC,
    double CorrectedCSample,
    float CConfidence,
    bool CorrectedA = false,
    double CorrectedASample = 0.0,
    float AConfidence = 0.0f);
```

`Accepted=false`(= `BeatLandmarkRefinement.Fallback`)는 fallback을 의미한다. 이 경우 기존 detector A/C를 그대로 사용한다.

## 모델 출력

최소 PoC 출력:

```text
a_offset_ms
c_offset_ms
a_confidence
c_confidence
```

권장 출력:

```text
a_offset_ms
c_offset_ms
a_confidence
c_confidence
b_as_a_risk
b_as_c_risk
```

`B` 자체를 화면에 표시할 필요는 없다. 하지만 모델이 B를 A/C와 구분해야 하므로 학습 label 또는 보조 output에 B risk를 포함하는 편이 설명력이 좋다.

구현된 ONNX 계약 (`OnnxBeatLandmarkRefiner`, `b2a85cc`): 입력은 엔벨로프 윈도우 `[1, N]`(A를 `WindowPreMs` 위치에 앵커), 출력은 `[a_off, c_off, a_conf, c_conf, ...]`로 **ms가 아니라 윈도우 샘플 단위 offset**이다(학습 데이터 `--export-training`의 `true_a_off`/`true_c_off`와 동일 프레임). `b_as_a_risk`/`b_as_c_risk`는 런타임 출력이 아니라 **학습 데이터 라벨**(`--export-training`의 `b_risk_a`/`b_risk_c`, 클러스터 스케일)로 들어간다. host가 출력을 절대 A/C 샘플로 매핑한 뒤 clamp/confidence를 적용한다.

## 보정 제한

TinyML이 틀렸을 때 detector 안정성을 망치지 않도록 보정은 강하게 제한한다.

권장 기본값:

- post-lock 상태에서만 적용한다.
- confidence가 threshold보다 낮으면 fallback한다.
- A 보정 범위는 후보 A 주변 작은 window로 clamp한다.
- C 보정 범위도 후보 C 주변 작은 window로 clamp한다.
- BPH acquisition과 PLL lock 입력은 우선 raw detector stream을 유지한다.
- metrics/display stream만 corrected A/C를 사용한다.

초기 clamp 예:

```text
A correction: candidate A 기준 [-8 ms, +2 ms]
C correction: candidate C 기준 [-4 ms, +6 ms]
```

이 값은 임시 시작점이다. synthetic fixture와 real sample 검증 후 조정한다.

## 기존 C-onset timing과의 경계

앱에는 이미 `UseCOnset`("Use C-onset timing") 설정이 있다. 이 설정과 landmark refiner는 둘 다 "C 시각을 앞으로 당기는" 동작이므로, 경계를 명시하지 않으면 서로 충돌하거나 보정이 이중으로 겹친다.

현재 동작(코드에서 확인됨):

- detector는 C를 낼 때마다 onset을 **항상** 계산한다(`src/TimeGrapher.Core/Detection/Detector.cs`의 `FindCOnset` 무조건 호출). 따라서 C 이벤트는 peak 시각과 onset 시각을 모두 들고 다닌다.
- onset은 C peak에서 50% threshold 지점까지 뒤로 걷는 backward-walk로 구한다.
- `UseCOnset`는 계산을 켜고 끄는 스위치가 아니라, metrics/display가 peak 시각을 쓸지 onset 시각을 쓸지 고르는 **선택 스위치**다(`src/TimeGrapher.Core/Analysis/DetectorMetricsEngine.cs`의 C event sample 선택). 끄더라도 onset 계산 자체는 그대로 돈다.

경계 결정(이 계획의 규칙으로 고정한다):

1. **refiner는 peak 전용 보정기다.** `CorrectedCSample`은 "보정된 C peak"를 의미한다. ML은 "어느 충격음이 진짜 C peak냐"만 책임지고, onset은 계속 결정론적 DSP가 담당한다.
2. **순서는 refiner -> onset 재유도다.** 보정된 peak를 기준으로 onset을 다시 계산하거나, 원래 peak->onset 거리를 그대로 유지해 평행 이동한다. onset을 더하지 않고 다시 그리므로 이중 보정이 생기지 않는다.
3. **clamp와 학습 정답은 peak 프레임으로 고정한다.** clamp 창(candidate C 기준 window)과 학습 label("true C offset")은 모두 peak 기준이다. 토글이 런타임에 바뀌어도 모델 정의가 흔들리지 않는다.

결과:

- refiner는 `UseCOnset` 상태를 알 필요가 없다(독립).
- onset backward-walk 로직은 바꾸지 않는다.
- `UseCOnset`는 순수하게 표시/측정용 선택으로 남는다.
- 삽입 위치는 `DetectorMetricsEngine`의 C event sample 선택 직전이다. C 이벤트의 peak/onset을 미리 보정해두면 기존 선택 코드는 바꾸지 않고 보정된 이벤트로 동작한다.

## 노이즈 리스크

TinyML landmark refiner는 시끄러운 환경에서 더 취약해질 수 있다.

특히 clean sample 위주로 학습하면 다음 문제가 생긴다.

- 약한 A 대신 B edge를 A로 과신한다.
- C가 약한 구간에서 B를 C로 과신한다.
- 노이즈 spike를 landmark로 착각한다.

따라서 모델은 fail-open이어야 한다.

```text
confidence 낮음 -> 기존 detector 값 그대로 사용
confidence 높음 -> 제한 범위 안에서만 보정
```

## 현재 sample 기준 worst cases

샘플 비교에서 다음 파일들이 검증에 중요하다.

- `sample/mine_adapter.wav`
  - noise floor 대비 weakest case
  - `refPeak/noise`가 가장 낮은 축
  - Beat Error가 크다.
  - TinyML robustness 테스트 1순위
- `sample/21600BPH_8215_InCase .wav`
  - 절대 신호 크기가 가장 작은 축
  - weak-signal 일반화 테스트에 적합
- `sample/mine_false.wav`
  - 21600 계열이 아니라 43200으로 false lock되는 adversarial 성격
  - landmark refiner가 lock 문제를 해결한다고 주장하면 안 된다.
  - false-lock 방어 또는 fallback 설명용으로만 쓴다.
- `sample/num.wav`
  - SNR은 높지만 Beat Error가 크다.
  - 노이즈 문제가 아니라 landmark bias 또는 실제 beat imbalance 확인용이다.
- `sample/mine.wav`, `sample/mine_usb.wav`
  - 같은 시계/비슷한 조건의 baseline 비교용이다.

## 구현 위치

### Core

추가/수정 예상:

- `src/TimeGrapher.Core/Detection/Scoring/IBeatLandmarkRefiner.cs`
- `src/TimeGrapher.Core/Detection/Scoring/BeatLandmarkCandidate.cs`
- `src/TimeGrapher.Core/Detection/Scoring/BeatLandmarkRefinement.cs`
- `src/TimeGrapher.Core/Analysis/DetectorMetricsEngine.cs`
- `src/TimeGrapher.Core/Analysis/BeatEventGateHost.cs` 또는 별도 `BeatLandmarkRefinerHost`

Core는 ONNX Runtime을 참조하지 않는다.

### Inference leaf project

추가 예상:

- `src/TimeGrapher.Inference/TimeGrapher.Inference.csproj`
- ONNX Runtime package reference는 이 project에만 둔다.
- `OnnxBeatLandmarkRefiner`가 `IBeatLandmarkRefiner`를 구현한다.

### Verify

추가/수정 예상:

- `src/TimeGrapher.Verify/AdverseScenarios.cs`
- `src/TimeGrapher.Verify/Program.cs`

`--gate`(이벤트 게이트)는 그대로 두고 별도 `--landmark` CLI를 추가했다(`425767e`). onnx 모델 경로 해석은 `--landmark=onnx:<path>`가 담당하며(`b2a85cc`), 모델 누락/로드 실패는 usage error(exit 2)다. `AdverseScenarios.cs`는 게이트 전용이라 손대지 않았다.

구현된 CLI:

```text
--landmark=off
--landmark=stub:noop
--landmark=stub:cpeak
--landmark=onnx:<path>
```

`stub`은 테스트와 demo를 위한 deterministic refiner다(`StubBeatLandmarkRefiner`). `cpeak`은 C를 검색 반경 내 엔벨로프 국소 최대값으로 스냅하는 휴리스틱으로, 정확도를 주장하지 않고 실제 ONNX 모델이 없을 때 pipeline을 검증하는 용도다(real sample에서는 detector가 이미 최대를 잡으므로 변화 없음 — `LANDMARK_REFINER_SAMPLE_REPORT.md`).

## 데이터와 학습

PoC는 synthetic data로 먼저 가능하다.

`WatchSynthStream`은 synthetic beat timing과 event side-channel을 만들 수 있다. 여기에 다음 변형을 추가하거나 활용한다.

- A 약화
- C 약화
- B가 A보다 큼
- B가 C보다 큼
- background noise 증가
- impulse noise
- pickup gain 변화
- beat error 주입

### 선행조건: WatchSynthStream 개조 (중요)

위 변형 중 핵심 4가지(A 약화 / C 약화 / B>A / B>C)는 **현재 `WatchSynthStream`으로는 만들 수 없다.** 데이터 생성에 들어가기 전에 생성기 개조가 선행되어야 한다. 코드에서 확인한 제약:

- 생성기는 A/B/C 구조를 만들기는 한다. `EnableRealisticPacket`이 A 온셋 클러스터, 중간 충격(B), C 클러스터를 넣는다(`src/TimeGrapher.Core/Sim/WatchSynthStream.cs`의 `WsStartPacket`).
- 그러나 각 클러스터 진폭이 **코드에 하드코딩**되어 있다. config의 진폭 노브는 전부 패킷 전체(`PcmPeakSignalLevel`, `PacketGainVariation`, `SignalLevelDrift`) 또는 C 전용(`CPeakAnchorGain`, `PostCLobeScale`)이다. **A만 약화하거나 B를 A/C 위로 올리는 노브가 없다.**
- `EnableCPeakLock`(기본 ON)이 의도적으로 C 앵커가 후속 링잉을 압도하게 설계되어 있어 B>C를 막는 방향이다.
- ground-truth 사이드채널(`WatchSynthStreamEvent`)은 **A 온셋 시각과 `AToCTimeS`만** 노출한다. true A/true C 라벨은 유도 가능(`true C = SampleIndex + AToCTimeS*fs`)하지만 **B landmark나 클러스터별 진폭 정답은 없다.**

따라서 학습 데이터 단계 진입 전에 다음을 별도 작업으로 분리한다.

1. A/B/C 클러스터별 진폭 config 노브 추가
2. B>A·B>C를 만들 수 있도록 (필요 시 C-peak-lock 우회 경로 포함)
3. 이벤트 구조에 B landmark(또는 클러스터별 진폭) 정답 노출

### 그 밖의 학습 리스크

- 이 repo에는 추론(ONNX Runtime) 계획만 있고 **모델 학습 스택(예: PyTorch + ONNX export)은 없다.** 데이터 생성은 C#로 가능해도 학습 루프/export 파이프라인은 별도 정의가 필요하다.
- **B 오인식(B->A)이 real sample에서 실재함을 진단으로 확인했다**(`LANDMARK_REFINER_BEAT_DIAGNOSIS.md`). 빈도가 신호 약화에 비례하며 약신호 worst-case(`mine_adapter.wav`)에서 만연하다. 학습 데이터 믹스는 이 분포에 맞춰 weak-A 비중을 ~64%로 보강했다(`b114099`). 다만 ground truth가 없어 "정확히 B"는 단정 불가다.
- `UseCOnset`가 켜진 경우 onset 타이밍이 이미 peak 지터에 강건하므로 C peak 보정의 *타이밍* 이득은 작아지고 *진폭* 이득만 남을 수 있다.

학습 label:

```text
window -> true A offset, true C offset, confidence target, optional B risk
```

실제 sample은 완전 자동 정답이 없으므로 다음처럼 다룬다.

- synthetic으로 모델을 먼저 만든다.
- real sample은 평가/데모용으로 사용한다.
- 필요한 경우 일부 구간만 사람이 A/C를 찍어 calibration set으로 만든다.

## 구현 순서

> **구현 현황 (2026-06-24):** 1~12단계 모두 main에 구현·검증됨. 유일한 외부 잔여 작업은 실제 ONNX 모델 학습(아래 "데이터와 학습" 참조). 관련 리포트: `LANDMARK_REFINER_SAMPLE_REPORT.md`(off vs stub real sample), `LANDMARK_REFINER_PI_LATENCY.md`(arm64 publish + latency).

1. ✅ `be5ca5f` Core에 `IBeatLandmarkRefiner` contract와 no-op(`NoOpBeatLandmarkRefiner`) 추가.
2. ✅ `7dee846` `DetectorMetricsEngine`에 optional refiner config(`BeatLandmarkRefinerConfig`) 추가.
3. ✅ `7dee846` A/C event pair를 beat candidate로 묶고 delayed envelope window를 넘기는 `BeatLandmarkRefinerHost` 추가.
4. ✅ `7dee846` confidence/fallback/clamp 정책을 host(Core)에서 적용.
5. ✅ `7dee846` corrected A/C를 metrics/display stream에만 흘림.
6. ✅ `7dee846` raw detector stream(`Result.Events`)은 그대로 유지(BPH/PLL 입력 불변).
7. ✅ `7dee846` scripted-refiner로 보정/clamp/fallback path unit test(stub 역할 겸).
8. ✅ `ab617b8`(B->C) `423fc8c`(B->A) synthetic weak-A/weak-C truth fixture로 보정이 truth에 수렴함을 증명. 생성기 선행 노브는 `91103b9`/`e0d2a89`.
9. ✅ `b2a85cc` `TimeGrapher.Inference`에 ONNX Runtime 기반 `OnnxBeatLandmarkRefiner` 추가(uses-view 갱신 `588a5c5`).
10. ✅ `791fb57`(stub) `425767e`(CLI) 배포용 `StubBeatLandmarkRefiner` + Verify `--landmark=off|stub:noop|stub:cpeak|onnx:<path>`.
11. ✅ `286c183` 5개 real sample off vs stub regression report(`LANDMARK_REFINER_SAMPLE_REPORT.md`).
12. ✅ `24e0884` linux-arm64 publish(arm64 native onnxruntime 포함) + latency smoke(`LANDMARK_REFINER_PI_LATENCY.md`).

> 9·11·12의 *실제 ONNX 모델* 경로는 학습된 `.onnx`가 있어야 런타임 검증된다. 학습 데이터 export(`--export-training`, `89b0483`)까지 완료했고, 모델 학습은 repo 밖(PyTorch 등) 작업이다.

## 테스트 전략

필수 unit tests (현황 — `BeatLandmarkRefinerTests`, `StubBeatLandmarkRefinerTests`, `BeatLandmarkRefinerSyntheticTests`, `OnnxBeatLandmarkRefinerTests`):

- ✅ no-op refiner는 기존 detector output(raw·metrics·display)을 동일하게 유지한다.
- ✅ low confidence / 거부 result는 fallback한다.
- ✅ correction은 clamp 창으로 제한된다(metrics만 이동, raw 불변).
- ✅ B->A synthetic fixture에서 corrected A가 truth에 가까워진다(oracle).
- ✅ B->C synthetic fixture에서 corrected C가 truth에 가까워진다(oracle).
- ⏳ "C correction의 amplitude 개선" / "A correction의 rate·beat-error outlier 감소"는 *타이밍 오차 수렴*으로 간접 증명된다(전용 metric 단언은 미작성; 실제 모델 단계에서 추가 권장).
- ✅ sync는 raw stream 기준 유지(refined 경로에서도 raw snapshot 불변·sync 유지, sync-loss 시 refiner reset).
- ✅ (ONNX) 출력 디코드(윈도우 offset -> 절대 A/C)와 모델 누락 가드. 실제 추론 경로는 학습된 모델이 있을 때 검증.

필수 integration/verify:

```powershell
dotnet test TimeGrapherNet.sln -c Release
dotnet run --project src/TimeGrapher.Verify -c Release -- --generated --byte-fixtures
dotnet run --project src/TimeGrapher.Verify -c Release -- --adverse
dotnet run --project src/TimeGrapher.Verify -c Release -- sample
```

ONNX path가 붙은 뒤:

```powershell
dotnet run --project src/TimeGrapher.Verify -c Release -- --generated --landmark=onnx:<model.onnx>
dotnet run --project src/TimeGrapher.Verify -c Release -- sample --landmark=onnx:<model.onnx>
```

## Acceptance criteria

다음 조건을 만족해야 한다 (현황 2026-06-24).

- ✅ 기본값 `landmark=off`(refiner 미설정)에서 기존 detector 결과가 바뀌지 않는다.
- ✅ Core는 ONNX Runtime 또는 UI/platform dependency를 갖지 않는다.
- ✅ TinyML(ONNX) implementation은 leaf project(`TimeGrapher.Inference`)에만 있다.
- ✅ weak-A synthetic case에서 A timing 오차가 개선된다(oracle로 ~2.4 ms -> <0.5 ms; median 기준. 실제 모델은 학습 후 정량화).
- ✅ weak-C synthetic case에서 C timing 오차가 개선된다(oracle로 ~5 ms -> <1 ms; amplitude 전용 단언은 미작성).
- ✅ `mine_adapter.wav`에서 confidence/fallback이 과보정을 만들지 않는다(stub에서 off와 동일, `LANDMARK_REFINER_SAMPLE_REPORT.md`).
- ✅ `mine_false.wav`는 비교에서 제외하여 false-lock 해결을 주장하지 않는다.
- ✅ Verify output에 off vs (stub) 비교가 남는다(`LANDMARK_REFINER_SAMPLE_REPORT.md`). onnx arm 비교는 모델 확보 후.
- ✅/⏳ linux-arm64 publish가 arm64 onnxruntime을 포함하고 host+stub latency는 block budget 안(`LANDMARK_REFINER_PI_LATENCY.md`). 실제 모델 추론 latency는 Pi에서 모델 확보 후 측정.

> 위 "개선" 항목은 **파이프라인 능력**(oracle/synthetic truth) 기준으로 충족된다. *실제 ONNX 모델*의 개선은 학습 후 별도 검증이 필요하다.

## 데모 메시지

채점/발표에서는 다음처럼 설명한다.

```text
TinyML은 detector를 대체하지 않는다.
기존 detector가 만든 beat 후보 주변에서 A/C landmark 위치만 보정한다.
이는 weak A 또는 weak C 때문에 B가 A/C로 잘못 선택되는 측정 품질 문제를 줄이기 위한 on-device inference이다.
confidence가 낮으면 기존 detector 값을 그대로 쓰므로, noisy 환경에서 모델이 과신해 전체 동기화를 망치지 않는다.
```

## 주의할 점

- TinyML이 BPH lock 문제를 해결한다고 말하지 않는다.
- `mine_false.wav` 같은 false-lock은 별도 robustness 문제다.
- 학습 데이터가 부족하면 실제 sample 개선은 제한적일 수 있다.
- 첫 구현은 PoC로 하고, synthetic truth에서 개선을 먼저 증명한다.
- real sample 개선 주장은 사람 라벨 또는 명확한 before/after metric이 있을 때만 한다.
