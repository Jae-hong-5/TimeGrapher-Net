# 프로젝트 작업 지침 (Agent Guide)

## 배경 (Context)

- 이 프로젝트는 그냥 배포를 위한 SW가 아니라, **소프트웨어 아키텍처 교육과정의 팀프로젝트 평가를 위한 과제**다.
- 원본 Qt/C++ 버전(TimeGrapher)을 **Avalonia + C# (.NET 8)** 로 포팅한 프로젝트이며, Windows와 Raspberry Pi 5(linux-arm64)를 단일 코드베이스로 지원한다.
- 평가와 인터뷰를 위해 사람이 누적된 수정 이력을 읽고 추적할 수 있어야 하므로, **모든 수정은 그 근거가 이력에 드러나야 한다.**

## 수정 범위 (Scope)

- 코드 수정 또는 구현 요청은 항상 필요한 범위 안에서 **최소한으로** 변경한다.
- 명시적으로 요청되지 않은 **예외 처리나 fallback 로직**은 임의로 추가하지 않는다.
- 명시적으로 요청되지 않은 **구조 개선이나 성능 개선을 위한 리팩터링**도 임의로 하지 않는다.

## 커밋 규칙 (Commits)

- 커밋은 항상 **논리적으로 분리 가능한 최소 단위**로 나누어 작성한다.
- 커밋 메시지의 **제목은 영어**로 작성하고 **Conventional Commits 규격**을 따른다.
  - 형식: `<type>(<scope>): <description>` — scope는 선택 (예: `feat(splash):`, `fix(install.sh):`, `docs:`, `chore:`, `test:`, `ci:`, `build:` 등)
  - `<type>`은 소문자로 작성한다.
- 본문은 **한글과 영어를 병기**하여 작성한다.
- 아키텍처에 영향을 주는 수정은 본문에 **어떤 소프트웨어 아키텍처 이론·전술에 근거한 수정인지** 명시하고, 필요하면 `docs/`의 해당 아키텍처 뷰 문서도 함께 갱신한다.

## 원칙 (Principles)

- 모든 수정은 **Software Architecture 원칙과 기존 구조에 근거**하여 수행한다.
- 아키텍처 구조와 결정 사항은 `docs/`에 문서화되어 있다 — 수정 전 관련 뷰를 먼저 확인한다:
  - `docs/MODULE_DECOMPOSITION_VIEW.md`, `docs/MODULE_USES_VIEW.md`, `docs/LAYERED_VIEW.md`, `docs/MVC_VIEW.md`, `docs/DATA_MODEL_VIEW.md`
  - `docs/SAP_TACTICS_ANALYSIS.md` (품질속성 전술), `docs/QT_CPP_TO_AVALONIA_PORTING.md` (포팅 근거)
- 레이어 의존 방향을 지킨다: `TimeGrapher.App` → `TimeGrapher.Core` ← `TimeGrapher.Platform.*` (Core는 UI·플랫폼에 의존하지 않는다).

## 빌드 / 테스트 (Build & Test)

```powershell
dotnet build TimeGrapherNet.sln -c Release        # 전체 빌드
dotnet test TimeGrapherNet.sln                    # 전체 테스트 (tests/ 하위 3개 프로젝트)
dotnet run --project src/TimeGrapher.App          # GUI 실행
dotnet run --project src/TimeGrapher.Verify -c Release -- --generated --byte-fixtures   # 헤드리스 검출 정확도 검증
```

- 코드 수정 후에는 관련 테스트가 통과하는지 확인하고 커밋한다.
