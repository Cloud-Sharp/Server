# Space Role Repository

Space별 Role과 Permission을 읽는 Core 포트다. [Space Role 유즈케이스](../use-cases/space-use-cases.md#2-space-role)

| ID | 작업·Core 포트 | 주요 호출 경로·입력 | 결과와 계약 | 현황 |
|---|---|---|---|---|
| RP-13 | Role 스냅샷 조회 · [ISpaceRoleReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceRoleReader.cs) · [SpaceRoleReader](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceRoleReader.cs) | RL-01, MB-03, IN-04·IN-05 · Space 내부 ID | 해당 Space의 Role과 Permission을 반환한다. 관리형 Role 목록, 교체 대상의 적격성, 초대 가입용 VIEWER 선택은 각 UseCase에서 판단한다. | 구현 |

[공통 Repository 계약](persistence-repositories.md#공통-계약)
