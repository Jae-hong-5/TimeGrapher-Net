# JD Documentation Work Package

이 폴더는 2026-06-20 논의한 TimeGrapher architecture 문서 작업을 실제 제출/정리 문서로 옮기기 위한 임시 작업 공간이다.

## Files

- `MODULE_USES_VIEW_JD.md`: 숙제의 dependency diagram 요구를 TimeGrapher C# 구조에 맞춘 module uses view 초안.
- `ADR-002-partial-pipe-and-filter.md`: 실시간 성능 제약 때문에 완전한 concurrent Pipe-and-Filter가 아니라 동기식 단계 체인으로 부분 적용하기로 한 결정 기록.

## Scope

- 문서 대상은 runtime source structure다.
- 모든 `.cs` 파일을 전수 diagram으로 나열하지 않고, project-level view에서 시작해 folder/module-level view로 내려간다.
- `bin/`, `obj/`, generated files, publish outputs는 dependency diagram scope에서 제외한다.
