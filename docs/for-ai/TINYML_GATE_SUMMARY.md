# TinyML 적용 결정 메모

> 최신 구현 방향: 약한 A가 onset 트리거 아래로 묻혀 B로 잘못 잡히는 문제는 결정론적
> phase-guided onset rescue로 해결했다(`PhaseGuideOnsetRescueScale`, App "Weak-A onset
> rescue" 토글). 그 반대 방향(잡으면 안 될 bad-data 후보 거부)은 이제 **TinyML drop-only
> 게이트로 구현됐다** — `TimeGrapher.Inference`의 `OnnxBeatEventGate`, App "TinyML Bad-Data
> Rejection" 토글. 자세한 현황은 아래 "현재 상태" 절.

## 결론

TinyML은 검출기 전체를 대체하는 방식으로 적용하지 않는다.

대신 기존 검출기는 그대로 두고, 검출기가 찾은 후보 이벤트 중 **잡음으로 보이는 것만 계산과 화면 표시 전에 걸러내는 방식**으로 적용한다.

```text
기존 검출기 -> 후보 이벤트 -> TinyML 후보 필터 -> 계산/화면 표시
```

이 방향은 성능 개선 여지를 만들면서도, 검출기 전체를 바꾸는 위험을 피하기 위한 선택이다.

## 왜 전체 검출기를 ML로 바꾸지 않는가

현재 검출기는 단순히 소리를 찾는 역할만 하지 않는다. BPH 탐지, PLL 동기화, A/C 이벤트 타이밍까지 함께 책임진다.

이 부분을 통째로 ML로 바꾸면 실패했을 때의 영향이 너무 크다. 모델이 틀리면 표시 숫자만 틀리는 것이 아니라, 동기화 자체가 흔들릴 수 있다. 또한 Raspberry Pi에서 실시간으로 충분히 빠른지, 다양한 잡음 조건에서 안정적인지까지 전부 다시 검증해야 한다.

그래서 이 프로젝트에서는 ML을 핵심 검출 로직의 주인으로 두지 않고, **기존 검출기가 만든 후보를 한 번 더 확인하는 보조 필터**로만 둔다.

## 해결하려는 문제

실제 문제는 "검출기 전체가 틀렸다"가 아니다.

시계 소리에는 진짜 틱/톡 말고도 짧고 강한 잡음이 섞일 수 있고, 이런 잡음이 A 이벤트처럼 잡히면 rate, beat error, amplitude가 순간적으로 튈 수 있다.

즉 해결하고 싶은 것은 **잡음 후보가 계산 결과를 오염시키는 문제**다. 이 문제에는 전체 ML 검출기보다 후보 필터 방식이 더 작고 안전한 해결책이다.

## 왜 이 방식이 더 안전한가

TinyML 후보 필터는 후보 이벤트를 통과시키거나 버릴 수만 있다.

하지 못하게 막아둔 일은 다음과 같다.

- 새 이벤트 만들기
- 이벤트 시간 수정하기
- BPH/PLL 동기화 입력 바꾸기
- 기존 검출기 대체하기

이 제한이 핵심이다. TinyML 모델이 틀려도 영향은 "후보 하나를 잘못 통과시키거나 잘못 버리는 것"으로 제한된다. 동기화 기준이나 검출기 내부 상태를 직접 흔들 수 없다.

## 현재 상태 (구현됨)

`IBeatEventGate` 소켓에 **실제 TinyML/ONNX 게이트가 구현·동봉됐다**: 리프 프로젝트 `TimeGrapher.Inference`의 `OnnxBeatEventGate`가 임베드 ONNX 모델(`Models/tick-quality.onnx`)로 후보의 128점 `BeatWindowFeatures` 윈도우를 *good escapement vs bad data*로 분류해 veto한다. 같은 소켓의 고전 구현 `PllMatchGate`도 그대로 남아 있다.

- **UI**: Settings의 `TinyML Bad-Data Rejection` 토글(기존 `PLL Event Veto` 옆). 켜지면 onnx 게이트가 PLL veto보다 우선해 설치된다.
- **검증**: `Verify --adverse --gate=onnx`(합성 행 INFO 측정) + `Verify <wav> --ab`(real off vs onnx A/B). 측정 — mine_false veto 36.5% vs clean watch 1.9~2.3%(~18배 차이).
- **모델**: dev 전용 `tools/TimeGrapher.GateTrainer`(sln 밖)가 real 녹음으로 SDCA 로지스틱 회귀를 학습(held-out AUC 0.994)해 ONNX로 export하고, 추론 어셈블리에 임베드한다.

남은 한계(정직): 모델이 **real 녹음으로 학습**돼 *합성* adverse 스트림에는 분포 밖으로 작동한다(약신호/잡음 행 과veto, postc-noise 합성 임펄스 무veto — sim↔real envelope 격차). real 입력에서의 bad-data 거부가 실효이며, 일반화는 real 라벨 확대에 달려 있다. 또 게이트는 검출 다운스트림이라 **BPH 자체(예: mine_false의 2배 lock)는 못 고친다** — 그건 detector 단 octave 디스앰비규에이션(별도 작업)의 몫이고, `postc-noise` adverse 행이 그 회귀 픽스처로 대기한다.

## 학습과 Raspberry Pi 사용 방식

TinyML을 실제로 쓰려면 학습은 필요하다.

다만 앱이나 Raspberry Pi에서 실시간으로 학습하는 구조는 아니다.

- PC에서 데이터 준비와 모델 학습을 한다.
- 학습된 모델을 `.onnx` 파일로 만든다.
- 앱이나 Raspberry Pi에서는 그 모델을 로드해서 후보 이벤트마다 추론만 한다.

Raspberry Pi에서도 사용할 수 있지만, 모델은 작게 유지해야 하고 실제 사용 가능 여부는 Pi에서 Verify 비교와 지연 시간 측정으로 확인해야 한다.

## 최종 판단

이 결정의 핵심은 **위험을 작은 범위에 가두는 것**이다.

TinyML을 검출기 전체에 넣으면 한 번에 너무 많은 것을 바꾼다. 반대로 후보 필터로 붙이면 기존 검출 구조와 동기화는 유지하면서, 잡음 때문에 측정값이 튀는 문제를 줄일 수 있다.

그래서 이 프로젝트에서는 TinyML을 "새 검출기"가 아니라 **기존 검출기를 보조하는 잡음 후보 필터**로 적용하는 방향이 더 적절하다.
