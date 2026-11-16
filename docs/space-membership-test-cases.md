# Space Membership 테스트 케이스

[Space Membership 유즈케이스 MB-01~MB-07](space-use-cases.md#3-space-membership)과 Membership·Role 부여 이력의 Core 유닛 테스트를 정리한다.

## 도메인 모델

[SpaceMembershipTests](../tests/CloudSharp.Core.Tests/Domain/Memberships/SpaceMembershipTests.cs) · [MembershipRoleAssignmentTests](../tests/CloudSharp.Core.Tests/Domain/Memberships/MembershipRoleAssignmentTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| Membership-01 | 같은 Space의 유효한 Role과 UTC 시각으로 Membership을 만든다. | UUIDv7 PublicId, `ACTIVE`, 버전 1로 생성한다. 각 할당에 Role ID·부여자·부여 시각을 기록한다. |
| Membership-02 | 0 이하 Space·사용자·부여자 ID, UTC 오프셋이 아닌 시각 또는 잘못된 Role 집합으로 생성한다. | 해당 도메인 오류를 반환한다. Role은 같은 Space에 속하는 저장된 활성 Role이어야 하며 하나 이상 필요하다. |
| Membership-03 | 세 상태의 스냅샷과 미저장 ID 0을 복원한다. `SUSPENDED`·`LEFT`의 빈 할당과 잘못된 저장값도 확인한다. | 저장값과 할당 내역을 보존하고 비활성 상태의 빈 할당을 허용한다. 유효하지 않은 스냅샷, 미정의 상태 또는 `null` 할당은 예외로 거부한다. |
| Membership-04 | 기존 Role 일부를 유지하면서 새 Role을 할당한다. 순서만 다른 같은 집합도 최대 버전을 포함해 적용한다. | 유지 Role의 이력을 보존하고 새 Role에만 새 부여자·시각을 기록한다. 실제 교체는 버전을 한 번 올리고 같은 집합은 스냅샷을 유지한다. 정지 중 Role 교체도 정지 상태를 보존한다. |
| Membership-05 | 활성 멤버를 정지한 뒤 활성화한다. 이미 정지된 멤버를 정지하거나 활성 멤버를 활성화하고, Role이 없는 정지 멤버도 활성화한다. | 정상 전이는 정지 시각을 기록·해제한다. 잘못된 전이는 실패한다. 할당이 없는 정지 멤버는 Role을 부여한 뒤 활성화할 수 있다. |
| Membership-06 | `ACTIVE`·`SUSPENDED` 멤버를 제거하거나 탈퇴시킨다. | `LEFT`와 탈퇴 시각을 기록하고 기존 Role 부여 이력을 보존한다. |
| Membership-07 | `LEFT` 상태의 변경, 잘못된 Role·부여자·시각, 최대 버전에서의 실제 변경을 시도한다. | 실패하며 기존 스냅샷을 유지한다. |
| Membership-08 | 원본 할당 컬렉션이나 Membership이 노출한 컬렉션을 변경한다. `MembershipRoleAssignment.Create`에 유효·무효 값을 전달하고 동등성을 비교한다. | 할당 컬렉션을 복사하고 읽기 전용으로 노출한다. 할당 값은 세 필드를 모두 비교하며 잘못된 ID·시각은 예외로 거부한다. |

## 멤버 변경 공통 검증

MB-03~MB-05는 [SpaceMembershipMutationTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipMutationTests.cs)를 상속한다. MB-06은 [제거 픽스처](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Removals/RemoveSpaceMemberUseCaseTests.cs)에서 해당 시나리오를 검증한다. 입력 검증과 의존성 미구성 경로는 [SpaceMembershipScaffoldingTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipScaffoldingTests.cs)에도 있다.

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 멤버-공통-01 | 올바른 RFC variant의 UUIDv7이 아닌 Membership PublicId 또는 0 이하 버전을 전달한다. | 각각 `MEMBER_PUBLIC_ID_INVALID` / `MembershipPublicId`, `SPACE_EXPECTED_VERSION_INVALID` / `ExpectedVersion`이다. 업무 조회·쓰기를 시작하지 않는다. |
| 멤버-공통-02 | MB-03~MB-05에 MCP 컨텍스트를 전달한다. 관리 요청에 `MEMBERS_MANAGE`가 없는 경우도 확인한다. | 각각 `AUTH_TOKEN_TYPE_NOT_ALLOWED`, `AUTH_SPACE_PERMISSION_DENIED`를 반환한다. |
| 멤버-공통-03 | 인가 뒤 Space가 사라지거나 최신 상태가 `ARCHIVED`·`DELETING`·`DELETED`다. | 각각 `AUTH_SPACE_NOT_FOUND`, `AUTH_SPACE_STATE_NOT_ALLOWED`다. 상태 오류에는 실제 상태가 담긴다. |
| 멤버-공통-04 | Membership이 없거나 다른 Space에 속하거나 현재 OWNER를 가리킨다. 같은 Space의 `LEFT` 멤버도 요청한다. | 대상 불일치는 `MEMBER_NOT_FOUND`, `LEFT`는 `MEMBER_INACTIVE`로 실패한다. 변경을 등록하지 않는다. |
| 멤버-공통-05 | 최신 저장 버전과 다른 `ExpectedVersion`을 보낸다. | `PRECONDITION_FAILED`를 반환하고 변경을 등록하지 않는다. |
| 멤버-공통-06 | Membership 조회 실패, 변경 등록 실패 또는 저장 시 동시성 충돌을 주입한다. | 조회·등록 실패는 `DEPENDENCY_UNAVAILABLE`을 전달한다. 등록 실패는 후속 저장 없이 롤백한다. 저장 충돌은 조회한 이전 버전으로 변경을 등록한 뒤 `PRECONDITION_FAILED`로 롤백한다. |
| 멤버-공통-07 | MB-03~MB-05에서 포트가 연결되지 않은 기본 생성자로 유효한 요청을 실행한다. | `DEPENDENCY_UNAVAILABLE`을 반환한다. 아래 업무 결과는 포트를 주입한 픽스처에서 검증한다. |

## MB-01 멤버 목록 조회

[ListSpaceMembershipsUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Listings/ListSpaceMembershipsUseCaseTests.cs) · [SpaceMembershipScaffoldingTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipScaffoldingTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-01-01 | `ACTIVE`·`ARCHIVED` Space를 조회하고 활성·정지 멤버가 있는 페이지와 빈 페이지를 받는다. | 읽기 포트에 Space ID·최신 OWNER ID·페이지 조건을 전달한다. 멤버 PublicId·사용자·상태·Role 및 다음 커서를 반환하고 빈 페이지는 `items=[]`, `nextCursor=null`이다. |
| MB-01-02 | 인가 후 Space가 `DELETING`·`DELETED`로 바뀌거나 목록 포트가 실패한다. | 각각 `AUTH_SPACE_STATE_NOT_ALLOWED`, `DEPENDENCY_UNAVAILABLE`을 반환한다. |
| MB-01-03 | 기본 Query와 한도 0·101, 공백 Cursor·Sort를 확인한다. | 기본 한도는 50이고 내림차순이며 Cursor·Sort는 `null`이다. 잘못된 값은 각각 `SPACE_LIST_LIMIT_INVALID` / `Limit`, `SPACE_LIST_CURSOR_INVALID` / `Cursor`, `SPACE_LIST_SORT_INVALID` / `Sort`다. |

## MB-02 멤버 상세 조회

[GetSpaceMembershipUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Details/GetSpaceMembershipUseCaseTests.cs) · [SpaceMembershipScaffoldingTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipScaffoldingTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-02-01 | `ACTIVE`·`ARCHIVED` Space의 활성·정지 멤버를 조회한다. | 최신 Membership과 버전을 읽어 멤버·사용자 PublicId, 상태, Role, 시각을 반환한다. |
| MB-02-02 | 미존재 Membership, 다른 Space의 멤버, 현재 OWNER를 요청한다. | 모두 `MEMBER_NOT_FOUND`를 반환한다. |
| MB-02-03 | Space가 `DELETING`·`DELETED`로 바뀌거나 상세 읽기 포트가 실패한다. 잘못된 PublicId도 전달한다. | 각각 `AUTH_SPACE_STATE_NOT_ALLOWED`, `DEPENDENCY_UNAVAILABLE`, `MEMBER_PUBLIC_ID_INVALID` / `MembershipPublicId`를 반환한다. |

## MB-03 멤버 Role 전체 교체

[ReplaceSpaceMemberRolesUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/RoleReplacements/ReplaceSpaceMemberRolesUseCaseTests.cs) · [멤버 변경 공통 검증](#멤버-변경-공통-검증)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-03-01 | `ACTIVE`·`SUSPENDED` 멤버의 Role 집합에서 하나를 유지하고 다른 Role을 교체한다. | 상태와 유지한 Role의 부여자·시각을 보존한다. 추가 Role에는 요청자와 현재 시각을 기록하고 이전 버전으로 등록해 저장·커밋한다. 결과는 갱신된 Role과 버전을 포함한다. |
| MB-03-02 | 순서만 다른 동일 Role 집합을 최신 버전·오래된 버전으로 각각 보낸다. | 최신 버전은 상태·시각·버전을 보존하고 저장 없이 성공한다. 오래된 버전은 `PRECONDITION_FAILED`다. |
| MB-03-03 | `LEFT` 멤버, 미존재·다른 Space·삭제된 Role, Role 읽기 실패를 각각 준비한다. | `LEFT`는 Role 읽기 전에 `MEMBER_INACTIVE`다. 부적격 Role은 `MEMBER_INVALID_ROLE`, 읽기 실패는 `DEPENDENCY_UNAVAILABLE`이다. 변경을 등록하지 않는다. |
| MB-03-04 | `RolePublicIds`에 `null`, 빈 집합, 중복 또는 유효하지 않은 UUID를 전달한다. | `MEMBER_INVALID_ROLE` / `RolePublicIds`를 반환한다. |

## MB-04 멤버 정지

[SuspendSpaceMemberUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Suspensions/SuspendSpaceMemberUseCaseTests.cs) · [멤버 변경 공통 검증](#멤버-변경-공통-검증)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-04-01 | 최신 버전의 `ACTIVE` 멤버를 정지한다. | `SUSPENDED`로 바꾸고 정지·수정 시각과 새 버전을 기록한다. 할당을 보존하며 이전 버전으로 등록해 저장·커밋한다. |
| MB-04-02 | 이미 `SUSPENDED`인 멤버를 정지한다. | `MEMBER_INVALID_STATE`를 반환하며 저장하지 않는다. |

## MB-05 정지 멤버 활성화

[ActivateSpaceMemberUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Activations/ActivateSpaceMemberUseCaseTests.cs) · [멤버 변경 공통 검증](#멤버-변경-공통-검증)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-05-01 | Role이 있는 최신 버전의 `SUSPENDED` 멤버를 활성화한다. | `ACTIVE`로 바꾸고 정지 시각을 지운다. 할당을 보존하면서 수정 시각·버전을 갱신하고 저장·커밋한다. |
| MB-05-02 | 이미 `ACTIVE`이거나 Role이 없는 `SUSPENDED` 멤버를 활성화한다. | 각각 `MEMBER_INVALID_STATE`, `MEMBER_INVALID_ROLE`을 반환하고 저장하지 않는다. |

## MB-06 멤버 제거

[RemoveSpaceMemberUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Removals/RemoveSpaceMemberUseCaseTests.cs) · [멤버 변경 공통 검증](#멤버-변경-공통-검증)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-06-01 | 최신 버전의 `ACTIVE`·`SUSPENDED` 멤버를 제거한다. | `LEFT`로 전환하고 탈퇴 시각을 기록한다. Role 할당 이력을 보존하고 새 버전을 반환한다. 이전 버전으로 변경을 등록해 저장·커밋한다. |

## MB-07 본인 탈퇴

[LeaveSpaceUseCaseTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/Leaves/LeaveSpaceUseCaseTests.cs) · [SpaceMembershipScaffoldingTests](../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipScaffoldingTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| MB-07-01 | `ACTIVE`·`ARCHIVED` Space의 본인 `ACTIVE`·`SUSPENDED` Membership으로 탈퇴한다. | 읽어 온 버전으로 `LEFT` 변경을 등록해 저장·커밋한다. 탈퇴 시각과 새 버전을 기록하고 Role 부여 이력을 보존한다. |
| MB-07-02 | 본인의 기존 `LEFT` 기록으로 탈퇴를 반복한다. | 성공하며 시각·버전을 보존하고 쓰기·저장을 생략한다. |
| MB-07-03 | 현재 OWNER, 관계가 없는 사용자, 다른 Space·사용자에 속한 Membership을 각각 준비한다. | OWNER는 `AUTH_SPACE_PERMISSION_DENIED`, 관계 미존재 또는 불일치는 `MEMBER_NOT_FOUND`다. |
| MB-07-04 | Space가 없거나 `DELETING`·`DELETED`일 때 요청한다. | 각각 `AUTH_SPACE_NOT_FOUND`, `AUTH_SPACE_STATE_NOT_ALLOWED`다. |
| MB-07-05 | 변경 등록 후 저장 실패를 주입한다. | `DEPENDENCY_UNAVAILABLE`을 그대로 반환하고 커밋 없이 롤백한다. |
| MB-07-06 | Actor 누락·잘못된 Session actor·MCP, 유효하지 않은 Space PublicId를 전달한다. | 각각 `SPACE_ACTOR_REQUIRED` / `Actor`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED`, `SPACE_PUBLIC_ID_INVALID` / `SpacePublicId`로 거부한다. |

[Space Membership 도메인 설계](../.llm/domains/space-membership.md) · [공통 Space 인가 테스트](space-test-cases.md#공통-space-인가-정책)
