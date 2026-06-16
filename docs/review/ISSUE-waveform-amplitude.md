# [이슈 분석 핸드오프] Waveforms 탭 진폭(amplitude) 계산이 lift angle 대신 오디오 엔벨로프 피크를 사용

> 작성 맥락: TimeGrapher-Net(C#/.NET/Avalonia, SW 아키텍처 과목 산출물). 최근 3시간 커밋 윈도(`1eb2568..HEAD`)에 대한 Claude 3 + Codex 3 적대적 독립 리뷰 → Codex 합의에서 **6/6 만장일치 + 합의 확정**된 HIGH 이슈. 이 문서만으로 재현·재검증·수정 판단이 가능하도록 작성됨.
>
> 기준 시점: 2026-06-16, HEAD = `65b31ff`, 워킹트리 clean.

## 1. 한 줄 요약

`WaveformCompareLogic.CalculateAmplitude`가 escapement 진폭 공식의 lift angle `λ`를 **고정 설정값(52°)이 아니라 오디오 엔벨로프 피크 × 360으로 계산**한다. 그 결과 Waveforms 탭의 밸런스휠 진폭이 **마이크 게인·신호 레벨에 비례하는 물리적으로 무의미한 값**이 된다.

## 2. 재현/재검증 명령 (repo 루트 `D:\TG1`)

```bash
git log --oneline -5 -- src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs
git grep -n "Samples.Span.ToArray().Max()" HEAD -- src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs
git show b1e4fe3 -- src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs
```

## 3. 현재 코드 상태 (HEAD, 미수정)

`src/TimeGrapher.App/Rendering/WaveformCompareLogic.cs:83-93`

```csharp
/// Amp = (3600 × λ) / (π × n × t_AC)
/// where λ = lift angle (degrees), n = beat rate (BPH), t_AC = A-to-C time (seconds).
private static double CalculateAmplitude(BeatSegment segment, int bph)
{
    double liftAngleDeg = segment.Samples.Span.ToArray().Max() * 360.0;   // ← 결함: λ를 엔벨로프 피크에서 산출
    double tACSeconds = (segment.CPeakOffsetMs - segment.AOffsetMs) / 1000.0;
    if (tACSeconds <= 0.0)
    {
        return 0.0;
    }
    return (3600.0 * liftAngleDeg) / (Math.PI * bph * tACSeconds);
}
```

호출 경로: `WaveformCompareRenderer.RenderLanes(snapshot, history)`(`:213`) → `LaneLabel(ticSeg, history?.Bph ?? 0)`(`:263/269`) → `CalculateAmplitude`. **`RenderLanes`는 `snapshot`을 쥐고 있어 `snapshot.LiftAngleDeg`에 접근 가능한데도 `bph`만 넘긴다.**

## 4. 왜 결함인가 (도메인 근거)

- **lift angle은 escapement 기하로 정해지는 고정 상수**다(통상 ~52°). 신호 진폭에서 유도할 수 없다.
  - 설정값 정의: `AnalysisWorker.cs:24` `public double LiftAngle = 52.0;`, `WatchMetrics.cs:12,22` `LiftAngle = 52.0;`
  - 입력 검증 범위: `WatchSynthStream.cs:54` "degrees. Watch lift angle, e.g. 44..60."
- `segment.Samples`는 **정류된(rectified) 디시메이트 오디오 엔벨로프**다(`BeatSegmentsSnapshot.cs` Samples 주석 "rectified, so values are non-negative"). 여기에 `× 360`을 곱해 "도(degree)"로 쓰는 것은 차원적으로도 무의미.
- 정확한 lift angle은 **이미 스냅샷에 실려 호출부까지 전달**돼 있다: `BeatSegmentsSnapshot.cs:148` `public double LiftAngleDeg { get; init; }` (주석: "Lift angle (deg) the producing analysis run was configured with").
- **영향:** 같은 비트가 Long-Term 탭(정식 `AmplitudeSample.PairAverageDeg`)과 Waveforms 탭에서 서로 다른 값으로 표시됨. Waveforms 값은 게인에 따라 출렁임.

## 5. Git 이력 / 오해 정정

- 이 결함을 만든 커밋: **`b1e4fe3` "feat(waveform): implement correct amplitude calculation using escapement equation"**, 작성자 **오선영(soh0221@lginnotek.com, 팀원)**, 2026-06-16 08:42, 오늘 아침 main에 통합됨.
- 그 커밋이 실제로 한 일: 예전의 "peak×360을 그대로 Amp로 표시"하던 코드를 escapement 공식으로 감쌌지만, **`Samples.Max()*360` 항을 그대로 들고 와 `λ`로 재명명**했다. 커밋 본문도 `λ = 리프트 각도(도) (피크 샘플 × 360)`라고 잘못된 전제를 명시.
- **"오늘 아침에 고쳐졌다"는 인식은 이 커밋을 가리키며, 실제로는 안 고쳐졌다.** HEAD에 `:85` 그대로 살아있고 이후 수정 커밋·스태시 없음.

## 6. 합의로 정리된 쟁점 — "공식 자체도 틀렸나?"

- Claude-A/B: 공식 형태가 앱 런타임의 정식 식과 다르다고 지적.
- Claude-C: 공식 형태는 요구사항 문서(`docs/requirement/TimeGrapher/Documents/TimeGrapher Equations_v1.md`)의 식과 일치한다고 지적.
- **합의 결론:** 둘 다 부분적으로 옳다. `(3600·λ)/(π·n·t_AC)`는 정식 식의 **소각(small-angle) 근사**이며, 실제 밸런스휠 진폭(~270°)에서 정식 식과 **크게 발산**한다.
  - 앱의 정식 식 `WatchMetrics.cs:220-223`:

    ```csharp
    public static double Amplitude(double liftAngle, double t1, double bph)
    {
        return liftAngle / Math.Sin((2.0 * Math.PI * t1) / (7200.0 / bph));
    }
    ```

  - 수학적 관계: `sin(x)≈x`이면 `λ/sin(2π·t₁/(7200/bph)) ≈ (3600·λ)/(π·bph·t₁)` → 신규 식은 정식 식의 선형화.
- **단, 핵심 결함은 공식 형태가 아니라 `λ`의 입력 소스다.** 공식 논쟁과 무관하게 lift angle을 엔벨로프에서 뽑는 것이 HIGH 결함.

## 7. 수정 시 활용 가능한 in-repo 정답 자원

1. `snapshot.LiftAngleDeg` (`BeatSegmentsSnapshot.cs:148`) — 설정된 실제 lift angle, 이미 `RenderLanes`까지 도달.
2. `WatchMetrics.Amplitude(liftAngle, t1, bph)` (`WatchMetrics.cs:220-223`) — 검증된 정식 진폭 함수(Core, UI 비의존).
3. `AmplitudeSample.PairAverageDeg` (Long-Term 그래프가 이미 그리는 값) — 재계산 없이 그대로 표시하는 선택지.

## 8. 수정 방향 후보 (새 세션이 판단)

- **(A) 최소 수정:** `LaneLabel`/`CalculateAmplitude`에 `snapshot.LiftAngleDeg`를 넘겨 `λ`로 사용. `Samples.Max()*360` 제거. 공식은 현행 유지(요구사항 문서 식).
- **(B) 일관성 우선:** 위에 더해 `CalculateAmplitude`를 `WatchMetrics.Amplitude` 호출로 대체 → 앱 전역에서 동일한 진폭 정의.
- **(C) 중복 제거:** Waveforms 탭이 자체 계산을 버리고 `PairAverageDeg`를 표시 → 탭 간 값 불일치 원천 차단.
- 아키텍처 주의: `TimeGrapher.Core`는 무의존이어야 하므로 계산 로직을 Core(`WatchMetrics`)에 두고 App은 호출만 하는 (B)/(C)가 의존 그래프상 더 안전.

## 9. 검증/테스트 계획 (현재 테스트의 허점 = 연동 이슈)

- 현 테스트 `WaveformCompareLogicTests.cs:54-67`은 `Assert.Contains("Amp:")`만 확인하고, fixture의 `Samples`가 전부 0이라 진폭이 `0.0°`로 출력돼 **어떤 수식이든 통과**한다(수치 미검증). → 이것 자체가 별도 MEDIUM 이슈(테스트 약화).
- 추가할 회귀 테스트: 고정 `LiftAngleDeg`(예 52), `bph`, A→C 간격으로 **수치 진폭을 단언**하고, **엔벨로프 샘플 크기를 바꿔도 결과가 변하지 않음**을 단언(게인 독립성 증명). 예시 입력 `λ=52, bph=28800, t_AC=0.009s`.
- 빌드/테스트: `dotnet build TimeGrapherNet.sln -c Release`, `dotnet test TimeGrapherNet.sln -c Release`.

## 10. 근거/신뢰도 (provenance)

- Claude 3 + Codex 3 독립 적대적 리뷰 **6/6 전원**이 동일 지점(`WaveformCompareLogic.cs:83-93`)을 HIGH로 지적, confidence 0.92–0.97.
- Codex 합의 라운드 최종: `ISSUE 1: CONFIRM | severity=high` (lift angle 소스 결함 + 런타임 식과의 발산 모두 확인).
- 본 분석 세션 직접 코드 확인 결과 HEAD에서 결함 라이브, 워킹트리 clean.

## 11. 새 세션이 결정할 열린 질문

1. 수정 방향 A/B/C 중 선택(과목 산출물의 "탭 간 일관성" 요구를 어디까지 볼지).
2. 요구사항 문서의 선형화 식을 유지할지, 정식 sin 식으로 통일할지(문서도 함께 갱신 필요할 수 있음).
3. 진폭 계산을 Core로 내릴지(의존 그래프/`SAP_TACTICS_ANALYSIS.md` 관점).
