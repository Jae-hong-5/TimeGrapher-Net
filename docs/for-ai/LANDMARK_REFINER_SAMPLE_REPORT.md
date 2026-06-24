# Landmark Refiner — Real-Sample Regression Report (off vs stub)

작성일: 2026-06-24

## 목적

TinyML landmark refiner 파이프라인이 실제 녹음(`sample/*.wav`)에서 기존 detector 결과를
망가뜨리지 않는지(과보정·회귀 없음) 확인하고, 결정론적 `stub:cpeak` refiner를 켰을 때의
변화를 off와 비교해 남긴다. 이는 계획서(`TINYML_LANDMARK_REFINER_PLAN.md`)의 acceptance
항목 "Verify output에 off vs tinyml 비교가 남는다", "`mine_adapter.wav`에서 confidence/
fallback이 과보정을 만들지 않는다"에 대응한다.

## 방법

동일 harness(`TimeGrapher.Verify`)로 각 샘플을 두 arm으로 실행했다.

```powershell
$samples = @("sample/mine_adapter.wav","sample/21600BPH_8215_InCase .wav","sample/mine.wav","sample/mine_usb.wav","sample/num.wav")
dotnet run --project src/TimeGrapher.Verify -c Release -- $samples --landmark=off
dotnet run --project src/TimeGrapher.Verify -c Release -- $samples --landmark=stub:cpeak
```

`off`는 refiner를 끈 기존 파이프라인, `stub:cpeak`는 C를 검색 반경 내 엔벨로프 국소
최대값으로 스냅하는 결정론적 refiner다(학습 모델 아님).

## 결과 (2026-06-24 실행)

모든 샘플이 BPH 21600으로 lock(sync=Synced)되었고, **off와 `stub:cpeak`의 metrics가
완전히 동일**했다(차이 0).

| 샘플 | Error Rate (s/d) | Amplitude (°) | Beat Error (ms) | off vs stub |
|---|---|---|---|---|
| mine_adapter.wav | -3.1 | 229 | 3.0 | 동일 |
| 21600BPH_8215_InCase .wav | +4.8 | 279 | 0.9 | 동일 |
| mine.wav | +26.3 | 219 | 1.4 | 동일 |
| mine_usb.wav | +25.9 | 218 | 1.3 | 동일 |
| num.wav | +43.9 | 178 | 6.9 | 동일 |

## 해석

- **회귀/과보정 없음**: `stub:cpeak`를 켜도 5개 샘플 모두 metrics가 그대로다. 특히
  worst-case인 `mine_adapter.wav`에서도 보정으로 인한 값 변화가 없어, host의
  clamp/confidence/fallback 정책이 과보정을 만들지 않음을 실제 샘플에서 확인했다.
- **stub은 real sample을 개선하지 못한다**: `stub:cpeak`는 "C를 국소 최대값으로 스냅"하는
  휴리스틱인데, detector가 이미 burst 후 최대 peak를 C로 고르므로 같은 지점을 다시 골라
  변화가 없다. 즉 이 stub은 *파이프라인이 real sample에서 안전하게 동작함*을 보이는 용도일
  뿐, 정확도 개선 도구가 아니다.
- **개선은 학습된 모델의 몫**: 실제 측정 품질 개선(예: 약한 A로 인해 B를 A로 잡는 beat의
  보정)은 국소 최대가 아니라 *진짜 landmark로 재배치*할 수 있는 학습된 `--landmark=onnx`
  모델에서 나온다. synthetic-truth 테스트(`BeatLandmarkRefinerSyntheticTests`)에서 oracle이
  오차를 붕괴시킨 것이 그 상한선을 보여준다.
- **정량적 "개선" 주장은 보류**: real sample은 자동 ground truth가 없어 off/stub/onnx 간
  "개선" 수치는 사람 라벨 또는 명확한 before/after가 있을 때만 주장한다(계획서 원칙).

## 재현

위 명령을 그대로 실행하면 동일한 비교가 Verify 콘솔 출력에 남는다. `mine_false.wav`는
43200으로 false-lock되는 별도 robustness 사례라 이 refiner 비교에서 제외했다(계획서와 동일).
