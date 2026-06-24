# Landmark Refiner — Real-Sample B->A Diagnosis

작성일: 2026-06-24

## 목적

계획서가 미뤄둔 핵심 질문 — **"B->A 오인식이 합성뿐 아니라 real watch에서도 실제로
일어나는가"** — 를 진단으로 확인한다. 이는 학습 투자(실제 ONNX 모델) 정당화의 전제다.

## 방법

real sample은 자동 ground truth가 없으므로 **B->A의 시그니처**를 측정한다(동일
`DetectorMetricsEngine` 경로, post-2s settle):

- **A 위상 잔차**: A index->time 선형 fit(레이트 제거) 후 parity별 평균 제거(beat error
  제거). 남은 잔차에서 B->A 비트는 A가 진짜 onset보다 ~2-4 ms 늦어 **양(+) 이상치**로 나타난다.
- **A->C 간격(진폭 proxy)**: A가 늦으면 A->C가 축소 → 같은 비트에서 **진폭 dip**.

두 신호가 같은 비트에서 일치하면 B->A로 본다. (측정은 빌드된 `TimeGrapher.Core`를 참조하는
scratch 진단으로 수행했다 — 현재 repo 미편입.)

## 결과 (2026-06-24, 모두 21600 계열)

| 샘플 | A 수 | 잔차 std | 최대 late | A>+2ms 늦음 | A->C 짧음(진폭 dip) | 판정 |
|---|---|---|---|---|---|---|
| `21600BPH_ST3600.wav` | 260 | 0.025 ms | +0.07 | 0 | 0 | clean (이상치 0) |
| `21600BPH_NH39A.wav` | 268 | 0.037 ms | +0.12 | 0 | 0 | clean (이상치 0) |
| `21600BPH_NH35.wav` | 261 | 0.270 ms | +3.44 | 1 (0.4%) | 1 | **드문 B->A** (t≈11.14s) |
| `sample/mine.wav` | 386 | 0.510 ms | +4.23 | 5 (1.3%) | 6 | **간헐 B->A** |
| `sample/mine_adapter.wav` | 177 | 1.451 ms | +2.88 | 30 (17%) | 75 (42%) | **만연 B->A** (약신호) |

- NH35는 한 비트(t≈11.14s)에서 A 잔차 +3.44 ms + 그 비트 A->C 6.99 ms(중앙값 10.83 대비
  −3.8 ms)로 두 시그니처가 일치.
- mine_adapter는 비트의 17%가 A>+2 ms 늦고 **42%가 A->C 짧음** — 약신호 worst-case에서 A가
  너무 약해 반복적으로 B를 A로 잡는다.

## 해석

- **B->A는 real watch에서 실재하고, 빈도는 신호가 약할수록 급증**한다(mine_adapter ≫ mine >
  NH35 ≫ clean 레퍼런스 0). clean 레퍼런스가 0이므로 detector 아티팩트가 아니다.
- 이는 refiner(특히 **A 보정**)가 노리는 바로 그 실패 모드다. 이 분포에 맞춰 학습 데이터
  믹스를 보강했다(weak-A 비중 ~64%, A-약화도 × 노이즈 스펙트럼 — commit `b114099`).
- 계획서의 **A 보정 우선** 결정이 합성에 더해 real sample로도 정당화된다.

## 한계

- ground truth가 없어 "그 늦은 충격이 정확히 B"라고 100% 단정은 못 한다 — *늦은 A + 진폭 dip +
  격자 이상치*가 일치하는 수준까지다.
- mine_adapter는 늦은 비트가 너무 많아 격자 fit 자체가 오염된다(잔차 ±양방향). 그 경우
  **A->C 짧음(42%)** 을 주신호로 본다.
- 진단 도구는 현재 scratch다. 학습 전후 비교를 재현 가능하게 하려면 repo에 정식 편입이 필요하다.
