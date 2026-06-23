# TinyML A/C Landmark Refiner 구현 메모

## 목적

이 문서는 다음 세션에서 TinyML 기반 A/C landmark 보정 기능을 구현할 수 있도록 현재 논의 내용을 정리한 것이다.

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

예상 interface:

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

예상 result:

```csharp
public readonly record struct BeatLandmarkRefinement(
    bool Accepted,
    bool CorrectedA,
    double CorrectedASample,
    bool CorrectedC,
    double CorrectedCSample,
    float AConfidence,
    float CConfidence,
    float BAsARisk,
    float BAsCRisk);
```

`Accepted=false`는 fallback을 의미하게 한다. 이 경우 기존 detector A/C를 그대로 사용한다.

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

기존 `--gate=onnx:<path>` reserved 문구는 landmark refiner용 옵션으로 바꾼다.

권장 CLI:

```text
--landmark=off
--landmark=onnx:<path>
--landmark=stub:<mode>
```

`stub`은 테스트와 demo를 위한 deterministic refiner이다. 실제 ONNX 모델이 없을 때 pipeline을 검증하는 데 쓴다.

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

학습 label:

```text
window -> true A offset, true C offset, confidence target, optional B risk
```

실제 sample은 완전 자동 정답이 없으므로 다음처럼 다룬다.

- synthetic으로 모델을 먼저 만든다.
- real sample은 평가/데모용으로 사용한다.
- 필요한 경우 일부 구간만 사람이 A/C를 찍어 calibration set으로 만든다.

## 구현 순서

1. Core에 `IBeatLandmarkRefiner` contract와 no-op implementation을 추가한다.
2. `DetectorMetricsEngine`에 optional refiner config를 추가한다.
3. A/C event pair를 하나의 beat candidate로 묶고, delayed envelope window를 refiner에 넘기는 host를 추가한다.
4. confidence/fallback/clamp 정책을 Core에서 적용한다.
5. corrected A/C를 metrics/display stream에만 흘린다.
6. raw detector stream은 diagnostics와 sync용으로 유지한다.
7. `stub` refiner로 B->A, B->C 보정 path를 unit test한다.
8. synthetic weak-A/weak-C fixtures를 추가한다.
9. `TimeGrapher.Inference` project에 ONNX Runtime 기반 implementation을 추가한다.
10. Verify CLI에 `--landmark=off|stub:*|onnx:<path>`를 추가한다.
11. `sample/mine_adapter.wav`, `sample/21600BPH_8215_InCase .wav`, `sample/mine.wav`, `sample/mine_usb.wav`, `sample/num.wav`로 regression report를 만든다.
12. Raspberry Pi 또는 linux-arm64 publish target에서 latency smoke를 확인한다.

## 테스트 전략

필수 unit tests:

- no-op refiner는 기존 detector output을 bit-equivalent로 유지한다.
- low confidence result는 fallback한다.
- correction clamp 밖의 output은 clamp 또는 reject된다.
- B->A synthetic fixture에서 corrected A가 truth에 가까워진다.
- B->C synthetic fixture에서 corrected C가 truth에 가까워진다.
- C correction이 amplitude 계산을 개선한다.
- A correction이 rate/beat-error outlier를 줄인다.
- sync acquisition은 raw stream 기준으로 유지된다.

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

다음 조건을 만족해야 한다.

- 기본값 `landmark=off`에서 기존 detector 결과가 바뀌지 않는다.
- Core는 ONNX Runtime 또는 UI/platform dependency를 갖지 않는다.
- TinyML implementation은 leaf project에만 있다.
- weak-A synthetic case에서 A timing median/RMS error가 개선된다.
- weak-C synthetic case에서 C timing 또는 amplitude error가 개선된다.
- `mine_adapter.wav`에서 confidence/fallback이 과보정을 만들지 않는다.
- `mine_false.wav`에 대해 landmark refiner가 false-lock 자체를 해결한다고 주장하지 않는다.
- Verify output에 off vs tinyml 비교가 남는다.
- Pi 배포 가능성을 위해 inference latency가 analysis block budget 안에 들어간다.

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
