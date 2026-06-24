# Landmark Refiner — linux-arm64 Publish & Latency Smoke

작성일: 2026-06-24

## 목적

계획서(`TINYML_LANDMARK_REFINER_PLAN.md`) 12단계 / acceptance "Pi 배포 가능성을 위해
inference latency가 analysis block budget 안에 들어간다"에 대응한다. (1) ONNX 추론
스택이 linux-arm64(Pi)로 publish되는지, (2) refiner 경로 오버헤드가 분석 블록 예산
안인지를 smoke로 확인한다.

## 1. linux-arm64 publish (Pi 배포 가능성)

```powershell
dotnet publish src/TimeGrapher.Verify/TimeGrapher.Verify.csproj -c Release -r linux-arm64 --self-contained false
```

publish 산출물에 **arm64 native ONNX Runtime**이 포함됨을 확인했다:

| 파일 | 비고 |
|---|---|
| `libonnxruntime.so` | arm64 네이티브 (~13.6 MB) |
| `libonnxruntime_providers_shared.so` | arm64 네이티브 |
| `Microsoft.ML.OnnxRuntime.dll` | 관리 래퍼 |

→ `TimeGrapher.Inference`(ONNX Runtime 포함)가 Pi(linux-arm64)로 배포 가능하다.

## 2. Latency smoke (host + stub 경로)

분석 블록 = 4096 샘플 @ 48 kHz = **85.3 ms/block** (실시간 예산). dev 머신(win-x64)에서
`sample/` 5개(총 **231.2 s** 오디오, ≈ 2709 블록)를 off vs `stub:cpeak`로 처리한 wall-time
(프로세스 시작/JIT 포함):

| arm | wall-time | 블록당 평균 | 실시간 배수 |
|---|---|---|---|
| `off` | ~5.3 s | ~2.0 ms/block | ~44x |
| `stub:cpeak` | ~8.6 s | ~3.2 ms/block | ~27x |

- 두 경로 모두 블록당 처리 시간이 85.3 ms 예산보다 **수십 배 작다** → 예산 안.
- `stub:cpeak`(windowed refiner)는 host의 delayed-envelope ring 버퍼링·beat 윈도우
  추출 때문에 off 대비 블록당 약 **+1.2 ms**를 더한다. 여전히 예산 대비 무시할 수준이다.
- 위 수치는 프로세스 시작/JIT를 포함한 보수적 값이라 정상 상태 블록당 비용은 더 작다.

## 한계 / 남은 일

- 이 측정은 **dev 머신(win-x64)의 host+stub 경로**다. Pi(arm64)는 CPU가 느려 실시간
  배수가 낮아지지만, 출발점 여유(수십 배)가 커서 예산 안일 가능성이 높다 — Pi 실측 필요.
- **실제 ONNX 모델 추론 latency는 아직 측정하지 못했다**(학습된 `.onnx` 모델 없음).
  publish는 런타임 배포 가능성을 증명할 뿐이다. 모델이 생기면 Pi에서 다음으로 프로파일한다:

  ```bash
  # Pi(linux-arm64)에 publish 산출물 + model.onnx 복사 후
  ./TimeGrapher.Verify sample/mine.wav --landmark=onnx:model.onnx
  ```

  TinyML 모델(작은 MLP, 입력 ~960 샘플)은 블록당 추론이 1회 미만(post-lock, beat당)이라
  예산(85 ms) 안에 들 것으로 예상되나, **Pi 실측으로 확정**해야 한다.
