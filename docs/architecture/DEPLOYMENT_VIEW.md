<!--
초안(Draft): 배포 뷰 섹션
- 대상: Milestone/ko/5-Architectural-Approaches.md 의 "3. MVC VIEW" 다음, "한 장으로 보는 아키텍처" 앞에 삽입 예정
- 삽입 시 번호를 "## 4. DEPLOYMENT VIEW" 로 확정하고 본 주석 블록은 제거할 것
- 렌더 이미지: 04.TimeGrapher-Net 의 deployment-view-detailed.drawio 를 PNG로 export → image/DEPLOYMENT.png 로 복사
-->

## 4. DEPLOYMENT VIEW – 배포 구조

**목적:** 하나의 코드베이스에서 만든 산출물(Target)이 **어떤 하드웨어 노드에, 어떤 형태로, 어떤 채널을 통해** 올라가는지 보여준다. 코드 구조가 아니라 "빌드된 결과물이 사용자에게 전달되는 경로"를 정의한다.

**핵심 개념:**
- **Git 중심 배포 파이프라인**: 다수 개발자가 각 PC에서 개발한 코드를 Git 서버로 모으고, CI/CD가 검증·산출물 생성·배포를 일관되게 처리한다.
- **단일 코드베이스 · 다중 타겟**: 같은 소스에서 RID(Runtime Identifier)별로 cross-publish 해 Windows / Raspberry Pi 로 배포한다.
- **런타임 무설치(self-contained)**: 모든 타겟은 .NET 런타임을 동봉한 단일 실행파일로 배포되어, 타겟에서 SDK·런타임 설치가 필요 없다.
- **LAN 배포 채널**: 생성된 Target은 Git 서버 네트워크(LAN)를 통해 연결된 각 노드로 배포·설치된다.

### 배포 다이어그램

```mermaid
flowchart TB
    subgraph Dev["💻 개발 환경 (다수 개발자 PC)"]
        DevPC["개발자 PC (VS Code)<br/>C#/.NET · Source Code"]
        Git["📦 Git Server (Origin)<br/>main / tags v*"]
        CICD["🤖 CI/CD Pipeline<br/>Push → build/test 검증<br/>tag v* → 배포 Target 생성"]
    end

    subgraph Target["📤 배포 Target (Release Assets)"]
        Artifacts["Target Artifacts<br/>win-x64 .zip · win-arm64 .zip<br/>linux-arm64 .tar.gz (Raspberry Pi)<br/>+ .sha256 (무결성)"]
    end

    Net(["🌐 Git 서버 네트워크 (LAN) — Target 배포 채널"])

    subgraph Win["🖥️ Windows PC (win-x64 / win-arm64)"]
        WinExe["TimeGrapher.App.exe<br/>단일 실행파일·무설치"]
        WinAudio["Platform.WindowsAudio<br/>NAudio / WASAPI"]
        WinIn["입력: USB 오디오 / USB Serial"]
        WinExe --> WinAudio --> WinIn
    end

    subgraph Pi["🍓 Raspberry Pi 5 (linux-arm64)"]
        PiExe["TimeGrapher.App<br/>ELF 단일 파일·무설치"]
        PiAudio["Platform.LinuxAudio<br/>ALSA (AGC off 권장)"]
        PiIn["입력: ALSA 오디오 / USB Serial"]
        PiExe --> PiAudio --> PiIn
    end

    subgraph Ext["🔊 외부 입력 신호"]
        Watch["⏰ 기계식 시계<br/>음향 비트 신호 (T1/T2/T3)"]
        Mic["🎙️ 마이크/픽업<br/>음향 → 전기신호"]
    end

    DevPC -->|git push| Git
    Git -->|CI/CD 트리거| CICD
    CICD -->|publish · Target 생성| Artifacts
    Artifacts -.->|Target 배포 등록| Net
    Net -.->|deploy · 배포·설치| WinExe
    Net -.->|deploy · 배포·설치| PiExe

    Watch -.->|음향| Mic
    Mic -.->|USB Audio| WinIn
    Mic -.->|USB Audio| PiIn

    style Dev fill:#F3E5F5,stroke:#7B1FA2,stroke-width:2px
    style Target fill:#FFF3E0,stroke:#E65100,stroke-width:2px
    style Win fill:#E8F5E9,stroke:#1B5E20,stroke-width:2px
    style Pi fill:#E8F5E9,stroke:#1B5E20,stroke-width:2px
    style Ext fill:#E3F2FD,stroke:#1976D2,stroke-width:2px
    style Net fill:#E0F7FA,stroke:#00838F,stroke-width:2px
```

<!-- 렌더 이미지 첨부 시: ![배포 뷰](image/DEPLOYMENT.png) -->

### 범례 (Notation)

선·점선·색상 세 가지 표기가 각각 독립된 의미를 가진다. 한 화살표는 **「색상(도메인) + 선 종류(흐름 성격)」** 조합으로 읽는다.

| 구분 | 기호 | 의미 |
|---|---|---|
| **선** | 실선 → | 직접 흐름 — 산출물 생성 · CI/CD 파이프라인 · 노드 내부 연결 |
| **선** | 점선 ⇢ | 전송 채널 — 네트워크 Target 배포 · 외부 신호 입력 |
| **색** | 🟪 보라 | 개발 환경 (개발자 PC · Git 서버 · CI/CD) |
| **색** | 🟧 주황 | 배포 Target (Release 산출물) |
| **색** | 🟦 청록 | Git 서버 네트워크 (LAN) |
| **색** | 🟩 녹색 | 타겟 실행 노드 (Windows · Raspberry Pi) |
| **색** | 🟦 파랑 | 외부 장치 / 신호 (시계 · 마이크) |
| **경계** | 점선 사각 | 실행/배포 그룹 경계 |

### 노드 / 산출물 매핑

| 노드(하드웨어) | RID | 실행 산출물 | 오디오 백엔드 | 배포 형식 |
|---|---|---|---|---|
| Windows PC | `win-x64`, `win-arm64` | `TimeGrapher.App.exe` (단일 파일) | `Platform.WindowsAudio` (NAudio/WASAPI) | `.zip` |
| Raspberry Pi 5 | `linux-arm64` | `TimeGrapher.App` (ELF 단일 파일) | `Platform.LinuxAudio` (ALSA) | `.tar.gz` |

### 배포 흐름 (3단계)

1. **개발·공유** — 다수 개발자가 각 PC에서 C#/.NET으로 개발하고, `git push`로 Git 서버에 코드를 모은다.
2. **검증·생성** — Git 서버는 push된 사항에 대해 CI/CD로 build/test를 검증하고, `tag v*`에서 타겟별(Windows / Raspberry Pi) 배포 Target을 생성한다.
3. **배포·설치** — 생성된 Target을 Git 서버 네트워크(LAN)를 통해 연결된 각 노드로 배포·설치한다.

런타임에는 별도의 외부 입력 경로가 있다: 기계식 시계의 **음향 비트 신호**가 마이크/픽업을 거쳐 전기신호로 변환되고, **USB 오디오**로 각 노드의 오디오 입력에 들어간다.

### 설계 결정과 근거 (품질 속성 연결)

| 특성 | 구현 | 근거(품질 속성) |
|---|---|---|
| 런타임 무설치 | `--self-contained` + `PublishSingleFile` | 라즈베리파이 등 타겟의 SDK 설치 부담 제거 |
| 단일 코드베이스·다중 타겟 | RID별 cross-publish + 플랫폼별 조건부 `ProjectReference` | 이식성, 변경 용이성 |
| 플랫폼 격리 | `TimeGrapher.Core`는 플랫폼 오디오에 비의존 (CI가 경계 검사) | 모듈성, 확장성 |
| 무결성 검증 | 산출물별 `.sha256` 동봉 | 배포 신뢰성 |

> **왜 이렇게?** 측정 정확도는 타겟 하드웨어(마이크·오디오 백엔드·터치스크린)에 직접 좌우된다. 그래서 "어디에 무엇을 어떻게 올리는지"를 코드 구조와 분리해 명시하고, **단일 코드베이스 → RID별 무설치 산출물 → LAN 배포**로 통일해 여러 타겟에서 동일한 동작을 보장한다.
