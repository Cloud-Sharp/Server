# Space Invite Repository

초대 복원·등록과 관리 목록, 수락 시 대상 Space 잠금 조회에 필요한 포트다. [Space Invite 유즈케이스](../use-cases/space-use-cases.md#4-space-invite)

| ID | 작업·Core 포트 | 주요 호출 경로·입력 | 결과와 계약 | 현황 |
|---|---|---|---|---|
| RP-17 | 초대 조회와 변경 등록 · [ISpaceInviteRepository](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceInviteRepository.cs) | IN-02~IN-05 · Space 내부 ID와 Invite PublicId 또는 `TokenHash` | 관리 조회는 Space 범위 내에서 PublicId로, 공개·수락 조회는 해시로 찾는다. 생성은 초대를 추가하고, 폐기·수락은 관찰한 초대 버전으로 변경을 등록한다. | 후속 |
| RP-18 | 초대 관리 목록 조회 · [ISpaceInviteReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceInviteReader.cs) | IN-01 · Space 내부 ID, cursor, limit, 정렬 필드·방향 | 해당 Space의 초대와 다음 cursor를 `SpaceInviteReadPage`로 반환한다. 토큰 해시를 제외한 관리 응답은 UseCase가 구성한다. | 후속 |
| RP-19 | 초대 대상 잠금 조회 · [ISpaceInviteTargetReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceInviteTargetReader.cs) | IN-05 · 쓰기 트랜잭션과 Space 내부 ID | [SpaceInviteTargetReader](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceInviteTargetReader.cs)가 Dapper로 `spaces` 행을 `FOR SHARE` 조회한다. 내부 ID, PublicId, 현재 소유자와 상태를 반환하며 행이 없으면 `null`이다. | 구현 |

- **수락 잠금.** RP-19는 같은 연결과 현재 트랜잭션을 사용한다. 활성 트랜잭션이 없거나 저장된 상태를 변환할 수 없으면 `InvalidOperationException`을 던진다. 알려진 가용성 장애는 `DEPENDENCY_UNAVAILABLE`이다. 잠금은 트랜잭션 종료까지 해당 Space 행의 변경·삭제와 충돌한다. IN-05는 최신 상태와 소유자로 수락 여부를 판단한다. [DapperDbSession](../../src/CloudSharp.Infrastructure/Persistence/Transactions/DapperDbSession.cs)
- **통합 테스트.** 잠금 중 별도 연결의 UPDATE가 실패하고 트랜잭션 종료 뒤에는 변경된 소유자·상태를 읽는지 PostgreSQL에서 검증한다. 테스트는 필요한 `spaces` 테이블을 직접 구성한다. [SpaceInviteTargetReaderIntegrationTests](../../tests/CloudSharp.Infrastructure.Tests/Persistence/Repositories/SpaceInviteTargetReaderIntegrationTests.cs)
- **초대 영속성 계약.** `space_invites.version`은 Core가 관리하는 동시성 토큰이다. 초대로 신규 가입할 때 IN-05가 같은 Space의 관리형 VIEWER Role을 조회해 부여한다. [Space·접근제어 스키마](../../.llm/database/spaces-access.md#space_invites) · [Space Invite 도메인](../../.llm/domains/space-invite.md)

[공통 Repository 계약](persistence-repositories.md#공통-계약)
