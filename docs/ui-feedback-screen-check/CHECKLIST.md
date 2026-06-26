# UI 피드백 — 화면별 Todo 체크리스트

기준: 각 그래프 탭의 기본 Font Size = 14 (DESIGN.md §3, 밀집 표는 13/11 예외 허용).
상태 표기: `[x]` 반영 완료(작업 트리) · `[ ]` 미반영 · `(답변)` 코드 변경 불필요·개념 질문 · `(시안)` 수정시안 존재 `docs/ui-feedback-value-readout-proposal.svg`.

> 현재 대부분 항목은 **커밋 전 작업 트리**에 이미 반영되어 있음(빌드 green). 미반영/설계 항목만 남음.

---

## Rate/Scope  ·  `tab-rate-scope`
- [x] 1. Signal Level 세로축 밀착·깜빡임 → 좌측 축 패널 52→68px (`RateScopeRenderer.ScopeLeftAxisSizePx`)
- [x] 2. Signal Level 단위 → `Signal Level (a.u.)` (a.u. = arbitrary units, 정규화된 신호 진폭)
- [x] 3. (답변) Beat Index = Beats 동일 개념(누적 비트 카운트). 축 라벨 `Beat Index`→`Beats` 통일 완료

## Beat Error  ·  `tab-beat-error`
- [x] 1. 라벨 Font 14 → 라벨 11→14, 값 15→16 (`InfoTabRegistry.cs` 패널)
- [x] 2. (시안) 수치값 가독성 개선 수정시안 제공 — `ui-feedback-value-readout-proposal.svg` (Beat Error/Escapement 카드)
- [x] 3. (답변) Beat Index = Beats 동일(위와 동일). 축 라벨 통일 완료

## Trace  ·  `tab-trace`
- [ ] 1. (답변) 가운데 회색 band = 롤링 평균 ±1σ(안정도 폭, `AddSigmaBand`), 점선 = 롤링 평균선(mean). 컬러(amber) band = 허용 공차 band

## Vario  ·  `tab-vario`
- [x] 1. Red dashed → Black dashed (현재값 라인 `LinePattern.Dashed` + `TextPrimary`, 범례 'Black dashed')
- [x] 2. Elapsed 왼쪽, View Criteria 버튼을 Elapsed 오른쪽 컬럼에 배치
- [x] 3. 그래프 가로폭에 맞춰 카드 좌우 margin 16→0 통일
- [x] 4. 글자/수치 가운데 정렬 (헤더·셀 `HorizontalAlignment/TextAlignment=Center`)

## Long-Term  ·  `tab-long-term`
- [x] 1. 하단 +/-, Live, Cur 리뷰 바 전체 제거 (`CreateLongTermReviewBar` 행 삭제)

## Sweep  ·  `tab-sweep`
- [x] 1. 1x/2x/3x 선택 버튼 붉은색 표시 (`SetActiveButtonClass` → `.active`)
- [x] 2. Signal Level 단위 `(a.u.)`
- [x] 3. Signal Level 세로축 밀착·깜빡임 → 축 패널 68px
- [x] 4. Instantaneous → Inst.
- [x] 5. 하단 수치 Font 14 (referenceText 12→14)
- [x] 6. (시안) 수치값 수정시안 — proposal SVG (Sweep/Beat Noise bottom readout)
- [x] 7. 2x 좌측 A,C 글/점선 깜빡임 → 마커 라인/라벨 가장자리 guard
- [x] 8. 3x 좌측 A,C 글/점선 깜빡임 → 동일 guard
- [ ] 9. (설계) 이 그래프의 목적/설정 — 미정. 설계 방향 필요

## Escapement  ·  `tab-escapement`
- [x] 1. 0 이하 음수영역 제거, 0 기준 정렬 (Y limits `0.0..top`)
- [x] 2. Signal Level 단위 `(a.u.)`
- [x] 3. Signal Level 세로축 밀착·깜빡임 → 축 패널 68px
- [x] 4. (시안) 수치값 수정시안 — proposal SVG
- [x] 5. 하단 수치 Font 14 (라벨 11→14, 값 15→16)
- [ ] 6. (설계) 이 그래프의 목적/설정 — 미정. 설계 방향 필요

## Position  ·  `tab-positions`
- [x] 1. 전반 Font 14 (표 셀 14, `PositionMinimumFontSize=14`; 헤더 11은 밀집 예외)
- [x] 2. `(−20 · 0 · +20 s/d)` 헤더 문구 삭제

## Health  ·  `tab-health`
- [ ] 1. 육각형 그래프와 Diagnosis 표의 세로 크기 맞춤 — 미반영
- [ ] 2. 전반 Font 14 확인 — rail이 13/12/11/9.5 혼재. 14로 상향 또는 예외 확정 필요

## Beat Noise  ·  `tab-beat-noise`
- [ ] 1. (설계) 이 그래프의 목적/설정 — 미정. 설계 방향 필요
- [ ] 2. 하단 미니그래프 A,C 각 2개 표시 — 미반영(로직)
- [x] 3. 선택 버튼 붉은색 (`SetActiveButtonClass`)
- [x] 4. (시안) 수치값 수정시안 — proposal SVG
- [x] 5. 하단 수치 Font 14 (averageText 12→14)
- [x] 6. Signal Level 단위 `(a.u.)`
- [ ] 7. A Marker 초록 점선 표시 — 미반영(렌더러)

## Waveforms  ·  `tab-waveforms`
- [x] 1. 우하단 Legend Tic/Toc 삭제 (`Legend.IsVisible=false`)
- [ ] 2. (시안) Current > Past 개선 — proposal SVG(Direction Marker) 존재, 실제 적용 미반영

## Filter Scope  ·  `tab-filter-scope`
- [x] 1. 4개 그래프 축 라벨 전체화 → 하단 2개만 Time(ms), 좌열만 Signal Level
- [x] 2. Signal Level 단위 `(a.u.)`
- [ ] 3. 가로/세로 눈금 깜빡임 — 좌축 68px로 일부 완화, 격자 tick 깜빡임 잔존(미해결)

## Sound Print  ·  `tab-sound-print`
- [ ] 1. (시안) 정보량 보강 — proposal SVG(overlay 제안: peak band/noise floor/dominant freq/live-head, 요약 strip) 존재, 실제 적용 미반영

## Spectrogram  ·  `tab-spectrogram`
- [ ] 1. (시안) 정보량 보강 — proposal SVG 존재, 실제 적용 미반영
- [x] 2. 선택 버튼 붉은색 (`SetActiveButtonClass`)

---

## 남은 작업(요약)
1. 설계 필요(목적/정보 보강): Sweep#9, Escapement#6, Beat Noise#1, Sound Print#1, Spectrogram#1
2. 렌더러 구현: Beat Noise#2(A,C 2개), Beat Noise#7(A 초록 점선), Waveforms#2(Direction Marker)
3. 레이아웃/폰트: Health#1(높이 맞춤), Health#2(폰트 14)
4. 잔여 버그: Filter Scope#3(격자 깜빡임)
5. 답변 항목(코드 무관): Trace#1, Signal Level 단위 의미

### 완료

- Beat Index→Beats 축 라벨 통일 (Rate/Scope#3, Beat Error#3)
