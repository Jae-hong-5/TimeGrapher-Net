# TimeGrapher-Net 개발 과정 (Development History)

> 이 문서는 **커밋 이력(커밋 메시지)과 현재 저장소 메타데이터만을 근거로** 프로젝트가
> 크게 어떤 흐름으로 개발되었는지 정리한 것이다. 코드 구조 설명이 아니라 "어떤
> 순서로, 왜 그렇게 만들어졌는가"를 graders·interviewers가 따라 읽을 수 있도록 한
> 서술이다.
>
> 집계 기준: 2026-06-05 ~ 2026-07-01, 커밋 **1361개**, 기여자 **9명**
> (`git shortlog -sn HEAD` 기준), 현재 제품 버전 **1.0.3**
> (`Directory.Build.props`). 버전 표기는 로컬 tag 객체가 아니라 release/version bump
> 커밋과 제품 메타데이터를 기준으로 삼았다.

---

## 한눈에 보기 (타임라인)

| 기간 | 단계 | 핵심 결과물 | 버전 |
|------|------|-------------|------|
| 6/5 | **0. 포팅된 상태로 출발** | Qt/C++ → Avalonia·.NET·C# 이식분이 최초 커밋. Windows + Raspberry Pi 동시 지원 | — |
| 6/6 ~ 6/9 | **1. 아키텍처 골격·규율 확립** | 레이어 경계, Core 무의존 원칙, 테마 단일화, 테스트·CI·릴리스 기반, AGENTS.md | v0.1.0 ~ v0.1.2 |
| 6/9 저녁 | **2. 성능 예산·계측** | 비트당 125ms 마감 예산, 점진적 저하(graceful degradation), 할당 제거·풀링, 지연 증거 계측 | — |
| 6/9 밤 ~ 6/10 | **3. 분석 그래프 탭 대량 생성** | Rate/Scope·Sound Print 위에 진단 탭을 대량 추가, NIHS 위치 카탈로그, FFT/STFT, 일시정지-리뷰 커서 | v0.5.0 |
| 6/11 ~ 6/12 | **4. 정리·견고성·검증 하네스** | 적대적 리뷰 정리 wave, 검출기 옵션 seam, Verify 골든마스터/악조건 시나리오 | v0.6.0 |
| 6/13 ~ 6/19 | **5. GUI 정교화** | Vario·Spectrogram·Waveform·Filter Scope·Positions 재설계, 실측 진폭, 이중언어 HTML 매뉴얼 | v0.6.1 ~ v0.7.4 |
| 6/20 ~ 6/21 | **6. 아키텍처 재정비: MVVM 전환 + ADR** | View → 컨트롤러/composition root 분리, ADR-001~004, 시퀀스·C&C·배포 뷰 | v0.7.5 ~ v0.7.9 |
| 6/22 ~ 6/23 | **7. v0.8 안정화와 글래스 UI 병합** | 샘플링 파라미터 설정화, 신호 품질 warning propagation, sapphire-crystal UI 병합, 로그 메타데이터 보강 | v0.8.0 ~ v0.8.2 |
| 6/23 ~ 6/25 | **8. 검출 개선 실험 → 보조형 신호 품질 구조** | weak-A rescue, spurious-beat acquisition gate, signal-quality DTO/classifier seam, Health 탭, Positions 재구성 | v0.9.0 ~ v0.9.1 |
| 6/25 ~ 6/27 | **9. 온디바이스 ONNX와 데모 UI 폴리시** | `TimeGrapher.Inference` leaf, ONNX+heuristic Strategy, live simulation knobs, 14px UI review, Compare/Health/Beat Noise/Sweep 폴리시 | v0.9.2 ~ v0.9.6 |
| 6/28 ~ 6/29 | **10. AI Analysis와 아키텍처 경계 강화** | Gemini backend analysis flow, Markdown renderer, credential-store policy, View adapter seam 보강, test hardening | v0.9.7 ~ v0.9.12 |
| 6/30 ~ 7/1 | **11. v1.0 릴리스 하드닝** | Linux audio probe/standard-rate policy, runtime stop/close bounds, measurement-log diagnostics, kiosk dialogs, markdown polish, README/manual sync | v1.0.0 ~ v1.0.3 |

> 사용자가 예상한 흐름 **① 포팅 → ② 아키텍처 구조 작업 → ③ 그래프 탭 → ④ GUI**
> 는 큰 줄기에서 맞는다. 다만 그 사이와 이후에 **성능 예산 작업**, **검증 하네스·검출
> 견고성 강화**, **MVVM 전면 리팩터링**, **신호 품질/AI 기능의 Strategy·Adapter
> 경계화**, **v1.0 릴리스 안정화**라는 비중 큰 단계가 더 있었다.

---

## 0. 출발점 — 이미 포팅된 상태로의 최초 커밋 (6/5)

프로젝트는 **빈 상태에서 시작하지 않았다.** 최초 커밋(`28ed25e` *Add
TimeGrapherNet application source*)부터 이미 **Qt/C++ 원본 시계 타이밍 측정기를
Avalonia UI · .NET · C#로 이식한 상태**로 들어온다. 전환 근거는 나중에
ADR-001과 포팅 문서로 명문화된다. 핵심 이유는 "크로스 빌드·릴리스 용이성(vs
Qt/C++)과 최종 사용자·개발자 사용성"이다.

출발 직후부터 데스크톱 한 곳이 아니라 **다중 플랫폼**을 겨눴다:

- Raspberry Pi 라이브 오디오 백엔드와 ALSA 폴백 (`cf4b348`, `3a2e764`)
- Pi 오디오 스모크 진단 (`de04f62`)
- 스플래시 화면과 포팅 문서 (`0da0789`)

즉 1번 가설("포팅된 상태로 최초 커밋")은 정확하며, 처음부터 **Windows + Linux/
Raspberry Pi 동시 배포**를 전제로 한 점이 더해진다.

---

## 1. 아키텍처 골격과 규율 확립 (6/6 ~ 6/9)

기능을 늘리기 전에 **구조와 작업 규율**을 먼저 세웠다. 사용자가 짚은 2번
("아키텍처 구조적 작업 먼저")에 해당한다.

- **레이어 경계 정리**: `11941f4` *Refine architecture boundaries and input
  lifecycle*, `3ce62e5` *Split Linux audio platform support* — 플랫폼 코드를
  어댑터 뒤로 분리. 이후 줄곧 지킨 의존 그래프
  (`App -> Core/Platform.*`, `Platform.* -> Core`, **Core는 무의존**)의 토대.
- **테마 단일화**: `86d7d66` *Consolidate theme colors into a single App.axaml
  source* — 색·브러시·폰트를 `App.axaml` 한 곳에서 흐르게 함. 그래프 팔레트도
  여기서 파생되도록 잡아, 이후 모든 UI 작업의 제약이 됨.
- **아키텍처 문서화 시작**: `b157261` SAP tactics/patterns 분석 문서, `7a507f2`
  아키텍처 뷰 다이어그램.
- **테스트·CI·릴리스 기반**: 메트릭 포매팅·DSP 필터·검출기 시나리오 테스트
  (125개 규모), `ed39b8a` 태그 트리거 릴리스 워크플로, `6a052f7` Linux 설치
  스크립트, `e18e658` win-arm64·linux-x64 타깃 추가.
- **에이전트 협업 규율**: `7a4a3e1` *AGENTS.md / CLAUDE.md* — "모든 변경은
  이력에 근거(rationale)를 남긴다", Conventional Commits, **영문+한글 이중언어
  커밋 본문**, 범위 외 버그는 직접 고치지 말고 보고 등 이 저장소의 작업 헌법을
  성문화. 이 프로젝트가 **소프트웨어 아키텍처 수업 평가 산출물**이기 때문이다.

-> **v0.1.0 ~ v0.1.2** (6/9).

---

## 2. 성능 예산과 측정 가능성 (6/9 저녁) — 가설에서 빠졌던 단계

탭을 쏟아내기 직전, **실시간 성능을 "예산"으로 다루는 작업**이 한 차례 있었다.

- **마감 예산 강제**: `c0afb80` *enforce the beat-period deadline with graceful
  degradation* — `AnalysisDeadlineMonitor`. 백로그를 비트 주기 단위로 환산해
  **2비트 예산**과 비교하고, 지속 초과 시 *시각 비용이 싼 순서*로 점진적 저하
  사다리를 오른다(사운드프린트 실시간 갱신 중단 -> 발행 간격 100->400ms -> 스코프
  데시메이션 2배). 회복 시 히스테리시스로 한 단계씩 복귀. SEI 성능 전술
  "실행 시간 한정 / 작업 요청 관리"를 명시적으로 적용.
- **핫패스 할당 제거·풀링**: 마커 컬럼 O(1) 조회, 사운드프린트 발행 스냅샷 풀
  회전, 검출기·메트릭 무할당화, 불변 rate-series 공유, 링버퍼 블록 복사
  (`622125f`~`f2c7f72`).
- **증거 기반 계측**: `7bd7130` 캡처/처리 시각 스탬프와 누락 비트 카운트,
  `3022e60` 상태바에 종단 지연·누락 비트 노출, `3571b80` 시간 단위 이력을 위한
  bounded `DecimatingSeries`.

이 단계가 뒤따르는 탭 폭증을 **실시간 안에서 버티게** 한 안전판이 된다.

---

## 3. 분석 그래프 탭 대량 생성 (6/9 밤 ~ 6/10)

사용자가 짚은 3번. 약 24시간 동안 **포팅분의 기본 2개 탭(Rate/Scope, Sound
Print) 위에 진단 탭을 대량 추가**했다. 패턴은 일관됐다. 먼저 Core/Rendering에
**순수(pure) 계산·렌더 로직**을 넣고, 그다음 그것을 탭으로 승격했다
(*convert the placeholder into a working tab*). 이후 Health 탭이 추가되면서 현재
앱 카탈로그는 README와 `InfoTabCatalog` 기준 **14개 표시 탭**이다.

이 시기에 추가·정착된 탭/진단 뷰:

1. **Trace Display** (rate+amplitude over time) — `831cf2d`
2. **Vario / Rate·Amp Stability** (running min/max/mean/stddev) — `eda5ee4`
3. **Beat Error Display & Diagnostic Trace** — `d3fb4ee`
4. **Scope Sweep** (1x/2x/4x sweep) — `22eef7e`
5. **Multi-Filter Scope** (F0-F3 필터뱅크 스택) — `2722b8f`
6. **Long-Term Performance** — `133d364`
7. **Test Positions** (NIHS 95-10 / ISO 3158 위치 카탈로그) — `4bb676e`
8. **Multi-Position Sequence** (이후 Positions 탭으로 통합) — `5dfb902`
9. **Beat-Noise Scope** (비트별 엔벨로프 세그먼트) — `c1a947a`
10. **Escapement Analyzer** (A->C 반복도) — `68d73e1`
11. **Waveform Compare** (현재 UI 명칭은 Comparison) — `ff36984`
12. **Spectrogram** (의존성 없는 radix-2 FFT + STFT) — `3edf93d`

이와 함께 **일시정지-리뷰(pause-and-review) 커서 계약**을 정의하고 모든 탭에
전파(`91f7f9b` 계열의 review-cursor contract, `14c5221`), 10개 자세 시퀀스 지원
(`b127b4a`)을 넣었다. 폭증 직후에는 곧바로 **검출 공백(detection gap)·리뷰
커서·자세 클릭 래칭** 등의 결함을 메우는 fix wave가 따랐다(`13f2b86`~`166cc5f`).

-> 문서·테스트 정합을 맞추고 **v0.5.0** (6/11).

---

## 4. 정리·검출 견고성·검증 하네스 (6/11 ~ 6/12) — 가설에서 빠졌던 단계

탭이 다 생긴 뒤, **품질을 끌어올리는 두 갈래 작업**이 집중됐다.

**(a) 적대적 리뷰 기반 정리 wave (6/11).** 수십 개의 `chore`/`fix`로 *write-only*
필드, 미사용 shim, 도달 불가 placeholder 체인, 죽은 제어 표면을 제거하고
(`8d9a22b`~`c9b76d9` 등), 테마 토글 시 마커 재색칠·Linux PCM 리더 격리 등
회귀를 메웠다. 이 시기 Avalonia 11.3.17 업그레이드로 테마 토글 텍스트 정렬
회귀를 해결(`220609b`).

**(b) 검출 견고성 + 검증 하네스 (6/12).** 원본 포팅 검출기를 건드리지 않으면서
견고성을 끼워 넣는 **seam** 설계가 핵심이었다:

- `f98e545` *TgDetectorOptions seam* — 모든 옵션 기본 off의 불변 레코드를
  생성자 오버로드로 주입. **`TgTypes.cs`는 "동결된 포팅 계약"**이라 건드리지
  않고 동일 정보를 전달. 변경용이성 전술(기존 인터페이스 유지 + 예상 변경 대비).
- 적응 floor·레짐 가드(`13cd9e9`, `d1eafdf`), **PLL 위상-매치 veto**를 메트릭
  choke point에 거는 `IBeatEventGate` seam(`4f86851`). 이 veto 계열은 6/24에
  실험·검증 뒤 제거되고, 현재 구조는 **비트를 버리지 않는 보조형 signal-quality
  classifier**로 정착한다.
- A/B로 효과 없던 옵션은 즉시 **revert**(`ccfccbc` NoiseCensor) — 측정으로
  판단하는 규율.
- **TimeGrapher.Verify**: 골든마스터 이벤트 시퀀스 핀(`0283dce`), 합성 fixture
  지상진실 채점(`0384980`), `--adverse` 악조건 시나리오(`b5bdda5`),
  `--fidelity-check`로 all-off 경로 = 베이스라인 동일성 보증(`74a7626`), CI에서
  악조건 A/B·fidelity 레인 상시 게이트(`63d6add`).

-> **v0.6.0** (6/12).

---

## 5. GUI 정교화 (6/13 ~ 6/19)

사용자가 짚은 4번. 가장 길고 커밋이 많은 구간으로, 각 탭을 **실제 시계 측정기
수준의 가독성·정확성**으로 다듬었다. 대표 작업:

- **Vario 재설계**: 요약 바, range-bar 게이지, 측정값별 판정(verdict). 이후
  좁은 폭 대응·폰트·간격을 수십 커밋에 걸쳐 미세 조정(6/13).
- **Spectrogram**: 테마 연동 viridis 컬러맵, dB 범위·창 메타데이터, 시간창
  선택·휠 줌·라이브 엣지 마커, Nyquist까지 전 스펙트럼.
- **Waveform**: tic-좌/toc-우 4 페어레인, **이스케이프먼트 방정식 기반 진폭
  산출**(`b1e4fe3`, 하드코딩 제거 -> 설정된 lift angle 사용), 비트 주기 클리핑.
- **실측 원파형**: `02ab8c9` 비트 세그먼트별 **실제 바이폴라 PCM** 캡처 -> RAW
  뷰·Escapement에 표시(그 전엔 처리된 엔벨로프였음).
- **Filter Scope**: 4레인 X축 줌/팬 링크, 2x2 그리드, peak-decimation.
- **Long-Term / Scope**: 패널 간 X축 링크, 허용범위 기준선, 고정 간격 시간 눈금.
- **Positions**: NIHS 용어 정렬, 일관성 대시보드·판정, **애니메이션 3D 모델**.
- **Settings 팝업**·타이틀바 정리·창 테두리(`f8e1c27`).
- **이중언어 HTML 사용자 매뉴얼** + 스크린샷 자동 캡처(`5e2f704`), 요구사항
  원본(Witschi 매뉴얼·FR 검토) 문서화.

이 구간에는 **되돌린 시도**도 있었다(이력의 솔직함). **위치별 그래프 누적**을
넣었다가(`5129118` 외) 전역 동작으로 **revert**(`680eb31`), 워치-자세 변경 시
그래프 리셋도 도입 후 revert(`b244cc0`). 팀 작업이라 이 구간 내내 feature 브랜치
<-> origin/main **머지 커밋**이 잦다.

-> **v0.6.1 ~ v0.7.4** (6/13 ~ 6/19).

---

## 6. 아키텍처 재정비 — MVVM 전환과 ADR (6/20 ~ 6/21) — 가설에서 빠졌던 단계

후반부는 "GUI 추가"가 아니라 **구조의 재정비**였다. 평가 산출물로서 **결정의
근거를 남기는 작업**이 집중됐다.

- **MVVM 전면 리팩터링(behavior-preserving)**: View 생성자가 서비스 그래프
  전체를 인라인 생성하던 것을 컨트롤러들로 분리하고, **`MainWindowBootstrapper`
  composition root**로 객체 그래프 배선을 한 곳에 모음(`b40ed09`, DI 컨테이너
  없이). accept-band·run-control·measurement-log·audio-device 등 책임을 각
  컨트롤러/presenter로 이동(`ad02e76`~`1ed89b7`), **MVVM 순수성 소스 가드
  테스트**로 고정(`a4adc37`).
- **ADR 정립**: ADR-001(Qt/C++ -> Avalonia UI 전환, `571523b`), ADR-002(worker
  레벨 pipe-and-filter), ADR-003(MVC 대신 **MVVM 채택**), ADR-004(App/test/
  verify 모듈 역할). 각 ADR을 품질속성 시나리오(QAS)와 연결.
- **아키텍처 뷰 확장**: run-lifecycle 시퀀스 뷰, Component-&-Connector 뷰, 배포
  뷰, MVVM 의존 뷰 — draw.io 소스와 SVG로 정리.
- **허용 밴드 중앙화**: `01cef3b`~`4635504` normal-range를 단일 편집 소스로 모아
  설정창에서 편집·재시작 간 영속·모든 그래프 라이브 반영.

-> **v0.7.5 ~ v0.7.9** (6/20 ~ 6/22).

---

## 7. v0.8 안정화와 글래스 UI 병합 (6/22 ~ 6/23)

6/22의 "마무리"는 실제 최종점이 아니라 **v0.8 라인 안정화의 출발점**이었다.

- **샘플링 파라미터 설정화**: 분석 블록 크기·캡처 버퍼 길이를 Settings에서 조정·
  영속화하고 run 시작 시 적용(`05f8de4`~`5e35286`).
- **v0.8.0 릴리스**: 문서·README·테스트 카운트를 코드와 정합하고 version bump
  (`d3bdc99`).
- **신호 품질 warning propagation**: graph warning을 frame과 UI까지 운반하는
  기반(`8f2faf1`)을 추가해 이후 신호 품질 UI의 표시 경로를 마련.
- **글래스모피즘 UI 병합**: `feat/glassmorphism-ui`에서 작업된 **sapphire-crystal
  glass layer**를 `App.axaml` 토큰 위에 얹고 메인창·설정 모달에 적용
  (`73b5177`, `5ff0bae`). 기존 팔레트를 덮지 않고 그 위에 얹는 방식.
- **로그 메타데이터 보강**: measurement log에 lift angle을 기록하고(`be8c819`),
  run 시작 시점의 lift angle을 캡처하도록 고정(`f91c755`).
- **v0.8.1 ~ v0.8.2**: 검출 gap·pending-ring·PLL 정밀도·theme fan-out 등 6/23
  회귀 테스트를 보강한 뒤 version bump(`0974b8b`, `51ee729`).

---

## 8. 검출 개선 실험에서 보조형 신호 품질 구조로 (6/23 ~ 6/25)

6/23~6/25는 "AI를 넣었다"라기보다 **어디에 판단을 둘 것인가**를 실험하고 정리한
구간이다. 핵심 결론은 현재 README에도 남아 있다. 신호 품질 판단은 **측정값을
삭제하거나 바꾸지 않는 advisory classifier**여야 한다.

- **실측 fixture와 false-lock 실험**: captured audio samples(`0c5c751`)를 추가하고
  irregular impulse false-lock guard(`97bf578`)를 시도했지만, 곧 revert
  (`b975c39`, `fed5cc9`). 효과 없는 방어 로직을 남기지 않는 규율이 유지됐다.
- **landmark refiner 실험 후 제거**: `IBeatLandmarkRefiner` 계약, stub refiner,
  `--landmark` CLI, ONNX refiner까지 시도(`be5ca5f`~`b2a85cc`)했지만, 실제 약한 A
  신호 문제에는 refiner보다 검출 단계 rescue가 맞다고 판단하고 unused stack을 제거
  (`4382151`, `a08d541`, `5b734a0`).
- **weak-A onset rescue 정착**: phase-guided onset rescue(`b64197d`)를 run parameter
  toggle로 노출(`75bca47`)하고, rescue strength slider(`362d2ac`)와 기본값 보정
  (`1458e5a`, `b48c446`)까지 이어진다.
- **spurious-beat acquisition gate**: BPH 획득 중 약한 가짜 비트를 거르는 gate를
  추가(`f15798e`)하고, Enhanced Auto BPH 설정으로 노출(`0051553`,
  `bba12e8`). tic/toc 비대칭·artifact ratio·gate ceiling 등을 테스트로 고정.
- **signal-quality classifier seam**: event veto/drop 방식은 제거(`9b407ec`,
  `f610f74`, `a7ae8f7`)하고, `ISignalQualityClassifier`와 feature extractor를 Core에
  둔 **보조 classifier seam**으로 전환(`3bebc01`, `9f29409`, `978be26`, `f652855`).
  이 구조는 "나쁜 데이터를 버리는" 전술이 아니라 "신뢰도를 설명하는" 전술이다.
- **Health/Positions 재구성**: Watch Health radar tab을 추가(`e087fed`)하고,
  consistency verdict를 순수 `ConsistencyDiagnosis`로 분리한 뒤(`bfc2fbe`),
  Positions는 데이터 테이블과 acquisition lane으로 단순화(`41e0fee`~`f035a85c`).

-> 이 흐름이 **v0.9.0 ~ v0.9.1**의 중심이다.

---

## 9. 온디바이스 ONNX와 데모 UI 폴리시 (6/25 ~ 6/27)

6/25 이후에는 보조형 classifier seam 위에 실제 온디바이스 모델과 데모 가능한 UI
폴리시를 얹었다.

- **`TimeGrapher.Inference` leaf 프로젝트**: `d8a1168`에서 ONNX signal-quality
  classifier를 별도 leaf 프로젝트로 추가하고, `08f5dfe`에서 App composition root가
  ONNX Strategy를 로드하되 실패하면 heuristic fallback을 쓰도록 주입. 현재 솔루션은
  `TimeGrapher.Inference`와 `TimeGrapher.Inference.Tests`를 포함한다.
- **Core 무의존 보존**: ONNX runtime은 `Inference` leaf와 App/Verify wiring에만
  머물고, Core에는 `ISignalQualityClassifier` 계약과 DTO만 남는다. 이 결정은 이후
  ADR-005(온디바이스 ONNX signal-quality classifier)로 문서화된다.
- **라이브 시뮬레이션 knobs**: rate error, amplitude, beat error, A/B/C cluster scale을
  run 중에도 바꾸도록 `WatchSynthStream`과 App forwarding을 확장(`21b41ef`~`5e366cc`).
  데모·검증 입력을 빠르게 바꾸는 support-user-initiative 전술이다.
- **UI 피드백 반영**: 14px base font, 왼쪽 패널 슬라이더 정렬, Filter Scope/Beat
  Noise/Health/Comparison 레이아웃, graph marker label, Scope Sweep 안정화 등을
  수십 개 fix로 닦았다(`a668c00`~`1574aa2`). 이때 Waveforms 탭은 현재 README의
  **Comparison** 명칭으로 정리된다(`b946796`).
- **Vario와 Health의 판정 언어 정리**: Witschi guidance에 맞춰 Vario verdict를
  단순화하고(`736b910`~`2eec4c7`), Health는 review-focused diagnosis로 설명을
  다듬었다(`1df0b7b`).
- **다국어 매뉴얼 확장**: 영어/한국어에서 더 나아가 Portuguese(pt-BR) 토글과 14개
  graph page 번역을 추가(`704871e`, `8e43d74`, `47445b0`).

-> **v0.9.2 ~ v0.9.6**.

---

## 10. AI Analysis와 아키텍처 경계 강화 (6/28 ~ 6/29)

6/28~6/29는 두 흐름이 겹친다. 하나는 **Gemini backend 기반 AI Analysis**, 다른 하나는
앞서 쪼갠 MVVM/Adapter 경계를 더 단단히 하는 작업이다.

- **AI Analysis flow**: Gemini backend security/setup/prompt/integration 문서를 쓰고
  (`db8358d`~`80dc11d`), App에 backend explanation flow를 추가(`9a77dca`). 이후
  이름을 **AI Analysis**로 정리(`ba7859b`)하고, 요청 상태·metadata/result 분리·영어
  backend response·dialog wording을 폴리시화했다.
- **Markdown renderer**: AI Analysis 결과를 Markdown으로 렌더링(`4d6d35e`)하고,
  v1.0.1~v1.0.3에서 heading/accent/font/inline span 처리까지 이어진다
  (`9efe7b7`, `a2dc660`, `814eebb`, `4395b22`).
- **credential/security boundary**: Raspberry Pi keyring, credential-store probe,
  persistent login target, consented log upload policy를 문서화하고(`48d839b`~
  `f924867`), backend analysis는 upload consent 없이는 실행하지 않도록 고정
  (`b81c3ae`).
- **MVVM·Adapter 경계 보강**: selection-ops adapter에서 MainWindow back-edge를 제거
  (`3b47c01`), dialog service 구현을 Views로 이동(`b7156ca`), theme toggle을
  view-model command로 라우팅(`fee3146`). ViewModel surface에 UI-framework leak가
  없는지 reflection test를 추가(`e6fc9e8`).
- **테스트·런타임 하드닝**: Windows-only test skip 정책, direct Core edge tests,
  unreadable ONNX model exception test, headless Avalonia serialization, weak assertion
  제거/강화(`741c007`~`b55519d`).
- **queue/stop/cleanup 안정화**: playback EOF finalization, analysis/logger queue bound,
  post-stop frame ignore, in-flight AI request cancel, run lifecycle cleanup hardening
  (`c4575ec`~`9254b9a`).
- **Health octagon radar**: Health를 8개 vertical axis octagon + CH/CB horizontal gauge
  strip으로 재구성(`598e5f4`, `8b50bcc`).
- **thread/signal flow 문서화**: signal processing flow와 thread data flow view를 추가
  (`d4afb5c`, `e8f1919`~`cd9efea`).

-> **v0.9.7 ~ v0.9.12**.

---

## 11. v1.0 릴리스 하드닝 (6/30 ~ 7/1)

마지막 구간은 새 기능보다 **운영 안정성·배포 신뢰성·문서 동기화**가 중심이다.

- **Linux audio policy 정착**: hardware sample-rate probe를 시도한 뒤, Pi에서
  신뢰할 수 없는 negotiated hardware rates를 숨기고(`8305dcb`, `8237a08`), 표준 live
  rates를 선택 가능하게 유지(`3075ace`). preferred USB mic volume 보정은 UI thread
  밖에서 실행하도록 바꿨다(`fbbfe90`).
- **architecture documentation 재배치**: `da53486` *docs: reorganize architecture
  documentation*으로 아키텍처 문서 위치와 공개 범위를 정리했다. 이후 ADR-002의
  structural diagram 제거(`8d0dca1`)처럼 평가 산출물의 읽기 흐름을 다듬는 커밋이
  이어진다.
- **close/stop boundedness**: wedged worker가 app close를 막지 않게 하고(`08ddec8`),
  stop teardown timeout을 cap(`3051628`). dialog도 kiosk 환경에서 top/focus를 유지
  (`0d9899c`, `ae59311`).
- **measurement/log diagnostics**: logger queue drop을 warning으로 표면화
  (`4c7a05d`)하고, run stop 뒤에도 dropped-row warning이 보이도록 고정(`51a2739`).
- **settings/default 일관성**: `AnalysisRunSettings` rescue-step default와 App default를
  맞추고(`b48c446`), null-safe converter를 Settings numeric field에 적용(`316cb49`).
- **rendering polish**: Beat Noise와 Escapement의 Y auto-fit은 수축만 smoothing하고
  확장은 즉시 반영하도록 수정(`73fc3bd`, `ee900ae`, `9a0fd82`, `a0c6926`). SAP 문서도
  해당 maintain-UI-consistency 전술을 반영(`d8ce943`, `4be8bf7`).
- **font/markdown portability**: Hack glyph 부족 시 bundled D2Coding fallback
  (`d504deb`), Markdown escape와 `***bold italic***` 처리(`f2e3369`), inline code accent
  chip(`4395b22`).
- **WAV writer close**: writer thread에서 WAV header를 bounded close로 finalize하도록
  고정(`8494fe7`).
- **문서 동기화**: README/한국어 README를 현재 코드 상태와 맞추고(`b6b0968`,
  `7bfe961`), Sound Print color key와 top-bar button manual page를 추가
  (`e5de77e`, `fa2c667`).

-> **v1.0.0 ~ v1.0.3**. 현재 제품 메타데이터는 `Directory.Build.props`의
`<Version>1.0.3</Version>`이다.

---

## 현재 상태 요약

- 현재 앱 카탈로그는 README와 `InfoTabCatalog` 기준 **14개 탭**:
  Rate/Scope, Beat Error, Trace, Vario, Long-Term, Sweep, Escapement, Positions,
  Health, Beat Noise, Comparison, Filter Scope, Sound Print, Spectrogram.
- 현재 솔루션에는 **6개 production project**가 있다:
  `TimeGrapher.Core`, `TimeGrapher.App`, `TimeGrapher.Inference`,
  `TimeGrapher.Platform.WindowsAudio`, `TimeGrapher.Platform.LinuxAudio`,
  `TimeGrapher.Verify`.
- 테스트 프로젝트도 6개다:
  App/Core/Inference/WindowsAudio/LinuxAudio/Verify tests. README의 최신 테스트
  설명은 `dotnet test` 기준 1346개 통과(App 867 / Core 391 / LinuxAudio 36 /
  Verify 27 / Inference 14 / WindowsAudio 11)로 정리되어 있다.
- 핵심 아키텍처 경계는 그대로 유지된다:
  `App -> Core / Inference / Platform.*`, `Inference -> Core`, `Platform.* -> Core`,
  `Verify -> Core`, **Core는 UI·OS·ONNX runtime에 의존하지 않는다**.
- 신호 품질 기능의 최종 형태는 **advisory ONNX classifier + heuristic fallback**이다.
  측정 이벤트를 drop/veto하지 않고, 사용자가 측정값을 얼마나 신뢰할지 판단하도록
  guidance를 제공한다.
- AI Analysis는 Gemini backend에 측정 로그를 보내 해설을 받는 별도 App 서비스 흐름이다.
  credential-store probe, upload consent, backend prompt contract, Markdown result
  rendering이 함께 정리되었다.

---

## 이력이 드러내는 일관된 특징

큰 단계와 별개로, 처음부터 끝까지 **관통하는 작업 방식**이 있다. 이 프로젝트가
"배포용 소프트웨어"이자 동시에 "아키텍처 수업 평가 산출물"이라는 성격에서 나온다.

1. **근거 남기는 커밋**: 거의 모든 의미 있는 변경이 **영문+한글 이중언어 본문**을
   달고, 어떤 **SAP/SEI 아키텍처 전술**(graceful degradation, 변경용이성,
   composition-root, pipe-and-filter, Strategy, Adapter, bounded execution 등)에
   근거하는지 명시한다.
2. **가장 작게 쪼갠 커밋 단위**: feat 1줄, 그에 딸린 docs 1줄, test 1줄을 따로
   커밋 — 추적 가능성을 최우선으로.
3. **반복적 적대적 리뷰 -> 즉시 fix/revert**: 정리 wave, golden-master·verify
   게이트, 효과 없는 false-lock/landmark/veto 시도의 측정 기반 제거가 반복된다.
4. **문서가 코드와 함께 진화**: architecture views, ADR, README, manual, SAP tactics,
   Gemini/AI policy 문서가 코드 변경과 함께 이동·추가·정리된다. 6/30에는 architecture
   documentation 재배치 커밋도 들어갔다.
5. **팀 협업의 흔적**: 9명 기여, feature 브랜치·PR·잦은 origin/main 머지, 일부 한글
   커밋(요구사항 검토·실험 결과 등)이 섞여 있다.
6. **릴리스 전 하드닝 케이던스**: v0.8 이후에는 기능 추가보다 테스트 강화, queue bound,
   stop/close timeout, Linux audio policy, dialog focus, markdown/font portability,
   README/manual sync가 집중되어 v1.0.3까지 이어졌다.

---

*근거: `git log`(1361 커밋, 2026-06-05 ~ 2026-07-01), `git shortlog -sn HEAD`,
`Directory.Build.props`, `README.md`, `README.ko.md`, `TimeGrapherNet.sln`,
`src/TimeGrapher.App/Tabs/InfoTabCatalog.cs`. 본문에 인용한 해시는 해당 변경을
대표하는 커밋이다.*
