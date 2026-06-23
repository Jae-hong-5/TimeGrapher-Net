# TimeGrapher-Net 개발 과정 (Development History)

> 이 문서는 **커밋 이력(커밋 메시지)만을 근거로** 프로젝트가 크게 어떤 흐름으로
> 개발되었는지 정리한 것이다. 코드 구조 설명이 아니라 "어떤 순서로, 왜 그렇게
> 만들어졌는가"를 graders·interviewers가 따라 읽을 수 있도록 한 서술이다.
>
> 집계 기준: 2026-06-05 ~ 2026-06-22, 커밋 **815개**, 기여자 **8명**
> (lgcmu2026-team5), 버전 태그 **v0.1.0 → v0.8.0** (19개).

---

## 한눈에 보기 (타임라인)

| 기간 | 단계 | 핵심 결과물 | 버전 |
|------|------|-------------|------|
| 6/5 | **0. 포팅된 상태로 출발** | Qt/C++ → Avalonia·.NET·C# 이식분이 최초 커밋. Windows + Raspberry Pi 동시 지원 | — |
| 6/6 ~ 6/9 | **1. 아키텍처 골격·규율 확립** | 레이어 경계, Core 무의존 원칙, 테마 단일화, 테스트·CI·릴리스 기반, AGENTS.md | v0.1.0 ~ v0.1.2 |
| 6/9 저녁 | **2. 성능 예산·계측** | 비트당 125ms 마감 예산, 점진적 저하(graceful degradation), 할당 제거·풀링, 지연 증거 계측 | — |
| 6/9 밤 ~ 6/10 | **3. 분석 그래프 탭 대량 생성** | 11개 분석 탭 추가(총 13탭), NIHS 위치 카탈로그, FFT/STFT, 일시정지-리뷰 커서 | v0.5.0 |
| 6/11 ~ 6/12 | **4. 정리·견고성·검증 하네스** | 적대적 리뷰 정리 wave, 검출기 옵션 seam·적응 floor·PLL veto·ML 소켓, Verify 골든마스터/악조건 시나리오 | v0.6.0 |
| 6/13 ~ 6/19 | **5. GUI 정교화** | Vario·Spectrogram·Waveform·Filter Scope·Positions 재설계, 실측 진폭, 이중언어 HTML 매뉴얼 | v0.6.1 ~ v0.7.4 |
| 6/20 ~ 6/21 | **6. 아키텍처 재정비: MVVM 전환 + ADR** | View → 컨트롤러/composition root 분리, ADR-001~004, 시퀀스·C&C·배포 뷰 | v0.7.5 ~ v0.7.9 |
| 6/22 | **7. 마무리** | 샘플링 파라미터 설정화, v0.8.0, 글래스모피즘(sapphire-crystal) UI | v0.8.0 |

> 사용자가 예상한 흐름 **① 포팅 → ② 아키텍처 구조 작업 → ③ 그래프 탭 → ④ GUI**
> 는 큰 줄기에서 맞는다. 다만 그 사이에 **② 직후의 성능 예산 작업**, **③ 직후의
> 검증 하네스·검출 견고성 강화**, **④ 후반의 MVVM 전면 리팩터링**이라는 세 개의
> 비중 큰 단계가 더 있었다(아래 2·4·6단계).

---

## 0. 출발점 — 이미 포팅된 상태로의 최초 커밋 (6/5)

프로젝트는 **빈 상태에서 시작하지 않았다.** 최초 커밋(`28ed25e` *Add
TimeGrapherNet application source*)부터 이미 **Qt/C++ 원본 시계 타이밍 측정기를
Avalonia UI · .NET · C#로 이식한 상태**로 들어온다. 전환 근거는 나중에
ADR-001과 포팅 문서로 명문화된다 — *"크로스 빌드·릴리스 용이성(vs Qt/C++)과
최종 사용자·개발자 사용성"* 때문이다.

출발 직후부터 데스크톱 한 곳이 아니라 **다중 플랫폼**을 겨눴다:

- Raspberry Pi 라이브 오디오 백엔드와 ALSA 폴백 (`cf4b348`, `3a2e764`)
- Pi 오디오 스모크 진단 (`de04f62`)
- 스플래시 화면과 포팅 문서 (`0da0789`)

즉 1번 가설("포팅된 상태로 최초 커밋")은 정확하며, 처음부터 **Windows + Linux/
Raspberry Pi 동시 배포**를 전제로 한 점이 더해진다.

---

## 1. 아키텍처 골격과 규율 확립 (6/6 ~ 6/9)

기능을 늘리기 전에 **구조와 작업 규율**을 먼저 세웠다 — 사용자가 짚은 2번
("아키텍처 구조적 작업 먼저")에 해당한다.

- **레이어 경계 정리**: `11941f4` *Refine architecture boundaries and input
  lifecycle*, `3ce62e5` *Split Linux audio platform support* — 플랫폼 코드를
  어댑터 뒤로 분리. 이후 줄곧 지킨 의존 그래프
  (`App → Core/Platform.*`, `Platform.* → Core`, **Core는 무의존**)의 토대.
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
  성문화. (이 프로젝트가 **소프트웨어 아키텍처 수업 평가 산출물**이기 때문)

→ **v0.1.0 ~ v0.1.2** (6/9).

---

## 2. 성능 예산과 측정 가능성 (6/9 저녁) — *가설에서 빠졌던 단계*

탭을 쏟아내기 직전, **실시간 성능을 "예산"으로 다루는 작업**이 한 차례 있었다.

- **마감 예산 강제**: `c0afb80` *enforce the beat-period deadline with graceful
  degradation* — `AnalysisDeadlineMonitor`. 백로그를 비트 주기 단위로 환산해
  **2비트 예산**과 비교하고, 지속 초과 시 *시각 비용이 싼 순서*로 점진적 저하
  사다리를 오른다(사운드프린트 실시간 갱신 중단 → 발행 간격 100→400ms → 스코프
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
Print) 위에 11개의 분석 탭을 얹어 총 13탭 카탈로그**를 완성했다. 패턴은 일관됐다
— 먼저 Core/Rendering에 **순수(pure) 계산·렌더 로직**을 넣고, 그다음 그것을 탭으로
승격(*convert the placeholder into a working tab*).

추가된 탭(생성 순):

1. **Trace Display** (rate+amplitude over time) — `831cf2d`
2. **Vario / Rate·Amp Stability** (running min/max/mean/σ) — `eda5ee4`
3. **Beat Error Display & Diagnostic Trace** — `d3fb4ee`
4. **Scope Sweep** (1x/2x/4x sweep) — `22eef7e`
5. **Multi-Filter Scope** (F0–F3 필터뱅크 스택) — `2722b8f`
6. **Long-Term Performance** — `133d364`
7. **Test Positions** (NIHS 95-10 / ISO 3158 위치 카탈로그) — `4bb676e`
8. **Multi-Position Sequence** (이후 Positions 탭으로 통합) — `5dfb902`
9. **Beat-Noise Scope** (비트별 엔벨로프 세그먼트) — `c1a947a`
10. **Escapement Analyzer** (A→C 반복도) — `68d73e1`
11. **Waveform Compare** — `ff36984`
12. **Spectrogram** (의존성 없는 radix-2 FFT + STFT) — `3edf93d`

이와 함께 **일시정지-리뷰(pause-and-review) 커서 계약**을 정의하고 모든 탭에
전파(`91f7f9b` 계열의 review-cursor contract, `14c5221`), 10개 자세 시퀀스 지원
(`b127b4a`)을 넣었다. 폭증 직후에는 곧바로 **검출 공백(detection gap)·리뷰
커서·자세 클릭 래칭** 등의 결함을 메우는 fix wave가 따랐다(`13f2b86`~`166cc5f`).

→ 문서·테스트 정합을 맞추고 **v0.5.0** (6/11).

---

## 4. 정리·검출 견고성·검증 하네스 (6/11 ~ 6/12) — *가설에서 빠졌던 단계*

탭이 다 생긴 뒤, **품질을 끌어올리는 두 갈래 작업**이 집중됐다.

**(a) 적대적 리뷰 기반 정리 wave (6/11).** 수십 개의 `chore`/`fix`로 *write-only*
필드, 미사용 셰임(shim), 도달 불가 placeholder 체인, 죽은 제어 표면을 제거하고
(`8d9a22b`~`c9b76d9` 등), 테마 토글 시 마커 재색칠·Linux PCM 리더 격리 등
회귀를 메웠다. 이 시기 Avalonia 11.3.17 업그레이드로 테마 토글 텍스트 정렬
회귀를 해결(`220609b`).

**(b) 검출 견고성 + 검증 하네스 (6/12).** 원본 포팅 검출기를 건드리지 않으면서
견고성을 끼워 넣는 **seam** 설계가 핵심이었다:

- `f98e545` *TgDetectorOptions seam* — 모든 옵션 기본 off의 불변 레코드를
  생성자 오버로드로 주입. **`TgTypes.cs`는 "동결된 포팅 계약"**이라 건드리지
  않고 동일 정보를 전달. 변경용이성 전술(기존 인터페이스 유지 + 예상 변경 대비).
- 적응 floor·레짐 가드(`13cd9e9`, `d1eafdf`), **PLL 위상-매치 veto**를 메트릭
  choke point에 거는 `IBeatEventGate` seam(`4f86851`) — 이는 **TinyML 추론
  소켓**으로도 열려 있다.
- A/B로 효과 없던 옵션은 즉시 **revert**(`ccfccbc` NoiseCensor) — 측정으로
  판단하는 규율.
- **TimeGrapher.Verify**: 골든마스터 이벤트 시퀀스 핀(`0283dce`), 합성 fixture
  지상진실 채점(`0384980`), `--adverse` 악조건 시나리오(`b5bdda5`),
  `--fidelity-check`로 all-off 경로 = 베이스라인 동일성 보증(`74a7626`), CI에서
  악조건 A/B·fidelity 레인 상시 게이트(`63d6add`).

→ **v0.6.0** (6/12).

---

## 5. GUI 정교화 (6/13 ~ 6/19)

사용자가 짚은 4번. 가장 길고 커밋이 많은 구간으로, 각 탭을 **실제 시계 측정기
수준의 가독성·정확성**으로 다듬었다. 대표 작업:

- **Vario 재설계**: 요약 바, range-bar 게이지, 측정값별 판정(verdict). 이후
  좁은 폭 대응·폰트·간격을 수십 커밋에 걸쳐 미세 조정(6/13).
- **Spectrogram**: 테마 연동 viridis 컬러맵, dB 범위·창 메타데이터, 시간창
  선택·휠 줌·라이브 엣지 마커, Nyquist까지 전 스펙트럼.
- **Waveform**: tic-좌/toc-우 4 페어레인, **이스케이프먼트 방정식 기반 진폭
  산출**(`b1e4fe3`, 하드코딩 제거 → 설정된 lift angle 사용), 비트 주기 클리핑.
- **실측 원파형**: `02ab8c9` 비트 세그먼트별 **실제 바이폴라 PCM** 캡처 → RAW
  뷰·Escapement에 표시(그 전엔 처리된 엔벨로프였음).
- **Filter Scope**: 4레인 X축 줌/팬 링크, 2×2 그리드, peak-decimation.
- **Long-Term / Scope**: 패널 간 X축 링크, 허용범위 기준선, 고정 간격 시간 눈금.
- **Positions**: NIHS 용어 정렬, 일관성 대시보드·판정, **애니메이션 3D 모델**.
- **Settings 팝업**·타이틀바 정리·창 테두리(`f8e1c27`).
- **이중언어 HTML 사용자 매뉴얼** + 스크린샷 자동 캡처(`5e2f704`), 요구사항
  원본(Witschi 매뉴얼·FR 검토) 문서화.

이 구간에는 **되돌린 시도**도 있었다(이력의 솔직함): **위치별 그래프 누적**을
넣었다가(`5129118` 외) 전역 동작으로 **revert**(`680eb31`), 워치-자세 변경 시
그래프 리셋도 도입 후 revert(`b244cc0`). 팀 작업이라 이 구간 내내 feature 브랜치
↔ origin/main **머지 커밋**이 잦다.

→ **v0.6.1 ~ v0.7.4** (6/13 ~ 6/19).

---

## 6. 아키텍처 재정비 — MVVM 전환과 ADR (6/20 ~ 6/21) — *가설에서 빠졌던 단계*

후반부는 "GUI 추가"가 아니라 **구조의 재정비**였다. 평가 산출물로서 **결정의
근거를 남기는 작업**이 집중됐다.

- **MVVM 전면 리팩터링(behavior-preserving)**: View 생성자가 서비스 그래프
  전체를 인라인 생성하던 것을 컨트롤러들로 분리하고, **`MainWindowBootstrapper`
  composition root**로 객체 그래프 배선을 한 곳에 모음(`b40ed09`, DI 컨테이너
  없이). accept-band·run-control·measurement-log·audio-device 등 책임을 각
  컨트롤러/presenter로 이동(`ad02e76`~`1ed89b7`), **MVVM 순수성 소스 가드
  테스트**로 고정(`a4adc37`).
- **ADR 정립**: ADR-001(Qt/C++→Avalonia UI 전환, `571523b`), ADR-002(worker
  레벨 pipe-and-filter), ADR-003(MVC 대신 **MVVM 채택**), ADR-004(App/test/
  verify 모듈 역할). 각 ADR을 품질속성 시나리오(QAS)와 연결.
- **아키텍처 뷰 확장**: run-lifecycle 시퀀스 뷰, Component-&-Connector 뷰, 배포
  뷰, MVVM 의존 뷰 — draw.io 소스와 SVG로 정리.
- **허용 밴드 중앙화**: `01cef3b`~`4635504` normal-range를 단일 편집 소스로 모아
  설정창에서 편집·재시작 간 영속·모든 그래프 라이브 반영.

→ **v0.7.5 ~ v0.7.9** (6/20 ~ 6/22).

---

## 7. 마무리 (6/22)

- **샘플링 파라미터 설정화**: 분석 블록 크기·캡처 버퍼 길이를 Settings에서 조정·
  영속화하고 run 시작 시 적용(`05f8de4`~`5e35286`).
- 문서·README·테스트 카운트를 코드와 최종 정합, **v0.8.0** 릴리스(`d3bdc99`).
- **글래스모피즘 UI**: 현재 작업 브랜치 `feat/glassmorphism-ui`에서 **sapphire-
  crystal 글래스 레이어**를 `App.axaml` 위에 토큰으로 얹고 메인창·설정 모달에 적용
  (`dba7c2e`~`9e64d75`). 기존 팔레트를 덮지 않고 그 위에 얹는 방식.

---

## 이력이 드러내는 일관된 특징

큰 단계와 별개로, 처음부터 끝까지 **관통하는 작업 방식**이 있다 — 이 프로젝트가
"배포용 소프트웨어"이자 동시에 "아키텍처 수업 평가 산출물"이라는 성격에서 나온다.

1. **근거 남기는 커밋**: 거의 모든 의미 있는 변경이 **영문+한글 이중언어 본문**을
   달고, 어떤 **SAP/SEI 아키텍처 전술**(graceful degradation, 변경용이성,
   composition-root, pipe-and-filter 등)에 근거하는지 명시한다.
2. **가장 작게 쪼갠 커밋 단위**: feat 1줄, 그에 딸린 docs 1줄, test 1줄을 따로
   커밋 — 추적 가능성을 최우선으로.
3. **반복적 적대적 리뷰 → 즉시 fix/revert**: 정리 wave, golden-master·verify
   게이트, 효과 없는 옵션의 측정 기반 revert가 주기적으로 반복된다.
4. **문서가 코드와 함께 진화**: `docs/for-ai/`(데이터모델·모듈uses·SAP전술),
   `docs/architecture/`, `docs/ADR/`를 코드 변경마다 동기화(`docs: sync...`
   커밋이 다수).
5. **팀 협업의 흔적**: 8명 기여, feature 브랜치·PR(#1·#3·#4)·잦은 origin/main
   머지, 일부 한글 커밋(요구사항 검토 등)이 섞여 있다.
6. **꾸준한 릴리스 케이던스**: 태그 트리거 CI가 4개 RID(win-x64/arm64,
   linux-x64/arm64)를 자동 빌드, v0.1.0 → v0.8.0까지 19개 태그.

---

*근거: `git log`(815 커밋, 2026-06-05 ~ 2026-06-22). 본문에 인용한 해시는 해당
변경을 대표하는 커밋이다.*
