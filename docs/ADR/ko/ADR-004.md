
---

# ADR004 - AI 활용, TDD 지원 및 팀 협업을 위한 App, Test, Verify 모듈 구조 분리

TimeGrapher 시스템은 향후 새로운 측정 지표나 그래프 분석 모드를 추가하기 위해 높은 수준의 수정 용이성(Modifiability)과 확장성이 요구됩니다. 현재 우리 팀은 6명의 인원이 동시에 하나의 코드베이스에서 작업하고 있으므로, 병렬 개발을 지원하고 코드 충돌을 최소화할 수 있는 구조가 필수적입니다. 또한, 온디바이스 AI 연동 및 AI 코딩 어시스턴트가 생성한 코드의 정확성을 담보하려면, 단위 동작을 자동으로 검증하는 TDD(테스트 주도 개발) 환경과 사람의 꼼꼼한 코드 리뷰가 필요합니다. 더불어 이러한 아키텍처 결정 사항을 팀 전체가 공유하여 소통의 낭비를 줄여야 합니다.

## Decision
*   **We will separate the fundamental project structure into independent `App`, `test`, and `verify` modules.**
    (우리는 프로젝트의 기본 구조를 `App`, `test`, `verify`의 독립적인 모듈로 분리할 것입니다.)
*   **We will adopt Test-Driven Development (TDD) practices to ensure that the core domain logic remains highly testable and UI-agnostic.**
    (우리는 핵심 도메인 로직이 UI와 분리되어 높은 테스트 가능성을 유지할 수 있도록 테스트 주도 개발(TDD) 방식을 채택할 것입니다.)
*   **We will enforce automated testing via the `test` and `verify` modules as a mandatory step before any code commit, providing a verified baseline for human review of AI-assisted code.**
    (우리는 AI가 작성하거나 연관된 코드에 대한 사람의 리뷰를 돕기 위해, 코드 커밋 이전에 `test` 및 `verify` 모듈을 통한 자동화된 테스트를 필수 단계로 강제할 것입니다.)

## Rationale
*   **협업의 효율성 (Collaboration Efficiency):** 6명의 팀원이 동시에 개발하더라도 핵심 도메인, UI, 테스트가 별도의 모듈로 물리적으로 분리되어 있으므로 병합 충돌을 크게 줄이고 독립적인 병렬 작업이 가능해집니다.
*   **AI 코드의 안전망:** AI가 제안하거나 생성한 코드에는 예기치 않은 오류가 포함될 수 있습니다. TDD를 통해 미리 작성된 테스트 코드들이 1차적인 안전망 역할을 하며, 6명이 서로의 코드를 리뷰할 때 테스트 통과 여부를 기준으로 더 객관적이고 효율적인 병합(Merge)을 진행할 수 있습니다.
*   **기각된 대안 (Rejected Alternative):** 단일 `App` 모듈 내에 모든 코드를 배치하는 방안도 고려되었습니다. 하지만 이 경우 AI가 대량으로 생성한 코드를 팀원들이 일일이 파악하고 수동으로 검증해야 하므로 코드 리뷰에 드는 부담(burden)이 지나치게 커지기 때문에 기각되었습니다.

## Status
Accepted

## Consequences
**Positive (긍정적 결과)**
*   최소한의 테스트 자동화를 통해 AI가 생성한 코드에 대한 팀원들의 리뷰 부담이 크게 줄어들며, 결과적으로 프로젝트의 전반적인 개발 속도가 향상됩니다.

*   자동화된 테스트 기반이 마련되어, 팀원이나 AI가 작성한 코드를 안심하고 메인 브랜치에 병합할 수 있는 신뢰성을 확보하게 됩니다.


**Negative (부정적 결과)**
*   TDD 원칙을 준수해야 하므로, 초기 기능 구현 시 팀원 모두의 학습 곡선(Learning curve)과 개발 시간이 다소 증가할 수 있습니다.
*   세 가지 모듈 간의 의존성을 관리해야 하며, 6명의 작업물이 모이는 CI(지속적 통합) 파이프라인과 ADR 문서를 지속적으로 최신 상태로 유지보수하는 부가적인 비용이 발생합니다.

---
