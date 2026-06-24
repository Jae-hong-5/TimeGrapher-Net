# TinyML 재구성: 민감한 트리거 + 후보 gate + PLL accept

작성일: 2026-06-24

## 왜 재구성하나 (실측 근거)

기존 방향(`TINYML_LANDMARK_REFINER_PLAN.md`)은 detector가 잡은 A/C를 **윈도우로 보고 제자리로
재배치(relocate)** 하는 refiner였다. 실험과 진단으로 두 가지가 드러났다:

1. **합성-only 모델은 real에 전이되지 않는다**(오히려 악화) — sim→real gap이 통계가 아니라 음향
   미세구조에 있음. (TimeGrapher-Refiner `RESULTS.md`)
2. **worst-case B->A는 "보이는 A를 B로 잘못 잡은" 게 아니라 "A가 음향적으로 묻혀 detector가 B에
   lock한" 것**이다. 오프라인 cadence-guided 라벨러로도 NH35·mine_adapter의 late 비트에서 약한
   A를 **0% 복구**(refPeak의 10% 미만, 노이즈 레벨). (`LANDMARK_REFINER_BEAT_DIAGNOSIS.md`)

→ **윈도우 relocate refiner는 worst-case에 구조적으로 안 맞는다**: 윈도우 안에 복구할 A 정보가
없는데 ML이 만들어낼 수 없다. 따라서 접근을 바꾼다.

## 재구성 파이프라인

```text
raw audio -> envelope
-> [1] 민감한 후보 검출 (낮은 onset threshold)   : 약한 A까지 후보로 (노이즈 spike도 함께 들어옴)
-> [2] 후보 분류 gate (TinyML)                  : 각 후보를 tick(A)/peak(C)/noise로 분류 + confidence
-> [3] PLL / 주기 accept (결정론)               : 분류 통과 + 위상·주기 일치 후보만 최종 A/C
        (후보 없음/전부 저신뢰 -> PLL phase로 위치 추정)
-> [옵션] offset refiner                         : 보이는 C/A peak를 미세 보정
```

핵심 전환: **"트리거를 ML로 대체"가 아니라 "트리거를 민감하게 열고 ML로 걸러낸다."**
민감한 threshold가 약한 A를 *후보로 만들고*(있다면), ML과 PLL이 진짜를 가린다. 이전 refiner는
이미 늦게 잡힌 A를 옮기려 했으나(정보 없음), 이쪽은 애초에 약한 A를 후보 단계에서 살린다.

## 단계별 책임과 기존 소켓 매핑 (이미 있는 것)

| 단계 | 책임 | 도구(이미 존재) |
|---|---|---|
| 1. 민감 후보 | 낮은 threshold로 약한 A/C 후보 다수 emit | detector threshold config(`OnsetFraction` 등) |
| 2. 분류 gate | 후보를 A/C/noise로 분류, 노이즈 후보 veto | **`IBeatEventGate`/`BeatEventGateHost`** |
| 3. PLL accept | 위상·주기 일치 후보 선택, 묻힌 A는 위상으로 배치 | detector `PhaseGuide*`, sync PLL |
| (옵션) offset | 보이는 C/A peak 미세 보정 | `IBeatLandmarkRefiner`/host |

**중요**: `IBeatEventGate`는 *원래부터* 이 용도로 설계됐다 — 그 파일 주석: "the TinyML socket:
classical ref impl is `PllMatchGate`; a future ONNX tick/noise classifier ... implements the same
interface." 즉 재구성은 새 소켓이 아니라 **이미 있는 gate(분류기) 소켓을 민감한 threshold와 함께
주력으로 쓰는 것**이고, relocate refiner는 보조(offset)로 격하된다.

## ML이 할 일 / 결정론이 할 일 (분담)

- **ML(gate)이 잘하는 것**: *보이는* 후보를 tick(A) vs noise impulse로 분류, C peak 위치/​offset
  regression, 저신뢰 후보 표시.
- **결정론(PLL/PhaseGuide)이 해야 하는 것**: 음향적으로 **묻힌 A** 배치 — 윈도우에 정보가 없으니
  ML이 아니라 PLL 위상으로 추정. 최종 주기/위상 일치 검증.
- 경계: 후보가 노이즈 레벨이면 ML 분류는 "노이즈 속 tick 찾기"라 어렵다 → real 데이터 품질이 관건.

## 데이터 (구속 조건 — 바뀌지 않음)

- **real 라벨이 필수다.** 시계 종류·마이크·gain·노이즈·약한 소리·충격음이 충분히 든 실측 데이터
  없이는 gate 분류기가 threshold보다 더 예측 불가능해진다(합성-only가 real을 악화시킴을 이미
  증명). 합성은 **pretrain 용도로만** 유효.
- 오프라인 cadence-guided pseudo-label은 *보이는* 후보엔 가능하지만, 묻힌 비트엔 불가(라벨러로
  확인됨).

## 한계 (정직)

- **트리거 아래의 약한 A**: 실측 결과 worst-case B->A의 A는 노이즈에 *묻힌* 게 아니라 노이즈
  위·트리거 아래에 있었다(NH35 envelope 0.0021 vs onset 트리거 0.0024). 따라서 **phase-guide
  창에서 트리거를 낮추는 결정론적 rescue로 복구된다 — 구현·검증 완료**(아래 "구현 현황"). ML
  불필요. 진짜로 노이즈 아래인 A만 복구 불가(그건 픽업/하드웨어 문제).
- **민감 threshold의 대가**: 후보가 급증(노이즈 포함) → gate 분류 부담↑ → 잘못 열면 false A가
  PLL/주기를 흔들 수 있다. gate accept 후에도 PLL 입력은 raw가 아니라 **검증된 후보**로 좁혀야
  안전.

## 단계적 구현 제안

1. detector에 **민감 후보 모드**(낮은 threshold로 후보 다수 emit) 추가 — 결정론, ML 무관.
2. 후보를 기존 `IBeatEventGate`로 흘려 분류·veto. 베이스라인은 결정론 `PllMatchGate`, ML은 leaf
   `TimeGrapher.Inference`의 ONNX 분류기(같은 인터페이스).
3. PLL accept + **phase-guided 배치 강화**(묻힌 A) — 결정론. 먼저 이것만으로 worst-case가 얼마나
   개선되는지 측정(`--diagnose`).
4. real 라벨 확보 후 gate 분류기 학습/검증. offset refiner는 보이는 케이스 한정 보조.

> 우선순위: 3번(결정론 phase-guided)은 데이터·ML 없이 당장 가능하고 약한-A에 직접 작용하므로
> **먼저** 시도한다. ML gate는 real 데이터가 모인 뒤. 이 순서가 헛수고를 막는다.

### 구현 현황 (2026-06-24)

**3번 구현·검증 완료** (commit `b64197d`): `PhaseGuideOnsetRescueScale` — opt-in, default 0 = off
(golden master bit-identical). post-lock phase-guide 창에서 onset 트리거를 설정 scale로 낮춰
약한 A를 잡는다. verifier `--diagnose --rescue=<scale>`로 측정.

scale 0.4 실파일 결과: late-A(>2ms) NH35 1→0, mine 5→0, **mine_adapter 30→0**, 최대 late
3.44→0.35 / 4.23→0.34 / 2.88→0.96 ms, A->C 진폭 dip 대부분 제거, **clean mine_usb 악화 없음**.
tradeoff: 약한 onset을 더 일찍 잡아 mostly-clean 시계에 작은 sub-ms scatter(NH35 residual std
0.27→0.79) — scale로 조절. 1·2번(ML gate)은 real 라벨 확보 후.

