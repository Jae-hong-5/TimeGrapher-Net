# 스트랩 없는 원형 시계 3D 모델

시계줄, 러그, 버클, 스트랩 구멍을 모두 제거하고 **동그란 케이스와 다이얼만 중심이 되도록** 정리한 버전입니다.
용두는 3시 방향을 알아보기 위한 표식으로 남겨두었습니다.

## 포함 파일

- `watch_model_round.glb`: PBR 재질을 유지한 권장 모델
- `watch_model_round_vertexcolor.glb`: 단일 메시/vertex-color 버전
- `watch_model_round_preview.png`: 미리보기
- `watch_positions.json`: 포지션별 기본 회전값
- `create_round_watch_model_blender.py`: Blender 4.x 재생성 스크립트
- `validation.json`: 내보낸 파일 구조와 크기 검증 결과

## 모델 기준

- 단위: meter
- 다이얼 앞면 법선: `+Z`
- 12시 방향: `+Y`
- 3시 방향 및 용두: `+X`
- 회전 원점: 케이스 중심 `(0, 0, 0)`
- 삼각형 수: 약 `3,540`
- PBR GLB 크기: 약 `60.6 KiB`

Avalonia 앱에서는 GLB 전체를 하나의 상위 노드로 취급해 그 노드의 quaternion을 `Quaternion.Slerp`로 보간하면 됩니다.
`watch_positions.json`의 회전 부호는 렌더러의 카메라/좌표계에 따라 한 번 조정할 수 있습니다.
