# Space Repository

Space의 집계 저장, 상세·목록·인가 조회, 생성 시 기본 리소스 등록에 필요한 포트다. [Space 유즈케이스](../use-cases/space-use-cases.md#1-space)

| ID | 작업·Core 포트 | 주요 호출 경로·입력 | 결과와 계약 | 현황 |
|---|---|---|---|---|
| RP-08 | Space 조회와 변경 등록 · [ISpaceRepository](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceRepository.cs) | SP-02·SP-04~SP-10, Membership·Invite 작업의 최신 상태 확인 · 내부 ID 또는 PublicId, 변경할 Space와 이전 버전 | `FindByIdAsync`·`FindByPublicIdAsync`는 독립적인 Space를 조회한다. `AddAsync`·`UpdateAsync`는 Unit of Work에 변경을 등록한다. 변경 충돌은 저장 시 판정한다. | 후속 |
| RP-09 | 상세 스냅샷 조회 · [ISpaceDetailsReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceDetailsReader.cs) | SP-03~SP-06·SP-10 · 인가된 Space 내부 ID | 최신 Space·버전과 소유자 및 루트 폴더 PublicId를 `SpaceDetailsSnapshot`으로 반환한다. | 후속 |
| RP-10 | 목록 후보 배치 조회 · [ISpaceListCandidateReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceListCandidateReader.cs) | SP-01 · actor, cursor, 배치 크기, 정렬 필드·방향 | 정렬된 후보와 `HasMore`를 반환한다. UseCase가 접근할 수 있는 후보로 응답 페이지를 채운다. | 후속 |
| RP-11 | 기본 리소스 등록 · [ISpaceBootstrapWriter](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceBootstrapWriter.cs) | SP-02 · 저장된 Space ID, 루트 폴더 PublicId, 관리형 Role 목록, 생성 시각 | 루트 DirectoryEntry·Folder와 기본 Role·Permission 연결을 Space 생성과 같은 Unit of Work에 등록한다. | 후속 |
| RP-12 | 인가 스냅샷 조회 · [ISpaceAuthorizationReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceAuthorizationReader.cs) | [SpaceAuthorizationService](../../src/CloudSharp.Infrastructure/Auth/Authorization/SpaceAuthorizationService.cs) · Space PublicId와 사용자 내부 ID | Space·요청자 Membership·연결 Role을 일관된 조회 범위에서 읽는다. Space 미존재는 `null`이다. Membership이 없거나 비활성이어도 존재하는 Space를 반환하며 인가 정책이 접근 자격을 판단한다. | 후속 |

[공통 Repository 계약](persistence-repositories.md#공통-계약)
