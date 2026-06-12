# Architecture Review Fixes

Date: 2026-06-06

이 문서는 아키텍처 리뷰 후 실제로 바꾼 내용을 짧게 정리한다. 핵심은 "오른쪽 정보 화면이 늘어나도 UI가 멈추지 않고, 분석 로직은 플랫폼과 분리되어야 한다"는 점이다.

## 적용 기준

- 한 번에 모든 탭을 렌더링하지 않는다.
- 현재 활성 탭만 비싼 화면 갱신을 한다.
- Core는 분석 결과를 만들고, App은 화면 배치와 refresh 정책을 가진다.
- 화면 갱신 일부를 건너뛰어도 그래프 데이터가 깨지지 않아야 한다.

## 주요 수정

### 1. 화면 갱신 밀림 방지

문제: 분석 결과가 빠르게 들어오면 화면 갱신 대기열이 밀릴 수 있었다.

수정:

- UI는 최신 분석 결과 중심으로 렌더링한다.
- 파형/오차 그래프는 누적 추가가 아니라 최신 묶음으로 교체한다.
- 분석 결과에는 지연, 대기 샘플, 누락 샘플 정보를 포함한다.

효과: UI가 늦어도 그래프가 중간 데이터 누락으로 깨지지 않는다.

### 2. 탭 구조 분리

문제: 탭이 늘어나면 `MainWindow`가 모든 탭을 직접 관리하게 될 위험이 있었다.

수정:

- `InfoTabCatalog`: 탭 ID, 제목, 갱신 간격 정의
- `InfoTabRegistry`: 실제 Avalonia 탭과 렌더러 생성
- `AnalysisFrameRouter`: 활성 탭에만 비싼 화면 갱신 호출

효과: 새 정보 탭을 추가할 때 고칠 위치가 명확해졌다.

### 3. 그래프 갱신 비용 축소

문제: 매 frame plot 객체를 다시 만들면 UI thread 비용이 커진다.

수정:

- ScottPlot plottable은 유지하고 데이터만 교체한다.
- `SeriesDataReducer`로 표시점 수를 줄인다.
- sound print 이미지는 매 frame 복사하지 않고 publish 간격을 둔다.

효과: 같은 데이터를 더 안정적인 비용으로 표시한다.

### 4. worker 시작/중지 안정화

문제: 이전 run의 callback이나 worker가 다음 run에 섞일 수 있었다.

수정:

- run session token으로 오래된 callback을 무시한다.
- Start/Stop/Pause 상태 전이를 ViewModel과 service로 분리했다.
- stop 경로는 timeout join과 writer close를 거친다.

효과: 시작/중지 반복 시 상태가 섞일 위험이 줄었다.

### 5. 플랫폼 경계 정리

문제: Windows audio와 Linux/Pi audio 차이가 분석 코드에 번질 수 있었다.

수정:

- `TimeGrapher.Core`는 NAudio, PipeWire, ALSA를 참조하지 않는다.
- Windows capture는 `TimeGrapher.Platform.WindowsAudio`에 둔다.
- Linux/Pi capture는 `TimeGrapher.Platform.LinuxAudio`에 둔다.
- `ILiveAudioWorker`와 `LiveAudioDevice` 계약은 Core 공유 계층에 둔다.
- `win-x64` publish는 WindowsAudio만, `linux-arm64` publish는 LinuxAudio만 참조한다.

효과: Core는 플랫폼 없이 테스트할 수 있고, 입력 방식은 교체 가능하다.

### 6. 스플래시 화면 추가

문제: 원본 MP4를 앱에서 직접 재생하면 코덱, 유료 MediaPlayer, Pi 의존성 문제가 생긴다.

수정:

- `intro.mp4`(원본)를 2배속한 `intro_2x.mp4`에서 640x360 PNG 74장으로 변환했다.
- `SplashWindow`가 30fps로 PNG 시퀀스를 보여준 뒤 `MainWindow`로 전환한다.
- 원본 MP4는 `Assets/Splash/Source/`에 보관하고, 앱 리소스는 PNG만 포함한다.

효과: Windows와 Raspberry Pi에서 같은 방식으로 시작 화면을 보여줄 수 있다.

### 7. 실행 lifecycle 분리

문제: `MainWindow`가 worker, buffer, run token, analysis thread 상태를 직접 소유했다.

수정:

- `RunSessionController`가 input worker, analysis worker, `MasterAudioBuffer`, run session token을 소유한다.
- `MainWindow`는 UI 선택, dialog, 상태 표시 연결만 담당한다.
- worker stop timeout과 stale callback 무시는 controller 경계로 옮겼다.

효과: 시작/중지 정책을 UI 코드에서 분리해 테스트와 확장 경계가 명확해졌다.

### 8. Core mutable state 축소

문제: `MasterAudioBuffer` 내부 ring buffer 상태와 `AnalysisFrame` 리스트 payload를 외부에서 바꿀 수 있었다.

수정:

- `MasterAudioBuffer` 내부 배열과 cursor는 private 필드로 닫고 snapshot/read/write API만 노출한다.
- `AnalysisFrame`의 series/marker 컬렉션은 App에 읽기 전용으로 노출하고 Core projector만 채운다.

효과: App/Platform 코드가 Core 내부 상태를 우회 변경할 가능성이 줄었다.

## 검증

통과한 확인:

```powershell
dotnet build .\TimeGrapherNet.sln -c Debug --no-restore
dotnet test .\TimeGrapherNet.sln -c Debug --no-build
dotnet publish .\src\TimeGrapher.App\TimeGrapher.App.csproj -c Release -r win-x64 --self-contained false --no-restore
dotnet publish .\src\TimeGrapher.App\TimeGrapher.App.csproj -c Release -r linux-arm64 --self-contained true --no-restore
```

결과:

- Windows build: 경고 0, 오류 0
- `TimeGrapher.Core.Tests`: 통과
- `TimeGrapher.App.Tests`: 통과
- Windows Release publish: `--smoke` exit code 0
- Windows GUI: 스플래시 후 `TimeGrapher` 메인 창 전환 확인
- Raspberry Pi: `./TimeGrapher.App --smoke` 통과
- Raspberry Pi GUI: `DISPLAY=:0` 실행 12초 유지, stderr 없음

## 남은 주의사항

- Pi에는 현재 capture source가 없어 live microphone 검증은 실제 USB mic 연결 후 다시 해야 한다.
- 새 정보 탭은 `InfoTabCatalog`와 해당 `IAnalysisFrameConsumer`를 추가하는 방식으로 확장한다.
- graph 탭은 화면 갱신 누락에 안전하도록 최신 묶음 교체 방식을 유지한다.
