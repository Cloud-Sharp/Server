# Space Role 테스트 케이스

[Space Role 유즈케이스 RL-01](../../use-cases/space-use-cases.md#2-space-role)과 Role·Permission 코드의 Core 유닛 테스트를 정리한다. 멤버에게 Role을 부여하거나 교체하는 동작은 [Space Membership 테스트 케이스](space-membership-test-cases.md#mb-03-멤버-role-전체-교체)에서 확인한다.

## 도메인 모델

[SpaceRoleTests](../../../tests/CloudSharp.Core.Tests/Domain/Memberships/SpaceRoleTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| Role-01 | ADMIN·MEMBER·VIEWER를 기본 템플릿에서 생성한다. | Space ID, UUIDv7 PublicId, 시스템 키, 관리형 플래그, 기본 이름·색상 및 생성·수정 시각을 확인한다. ADMIN은 정의된 모든 권한을 받고, MEMBER는 VIEWER 권한을 포함해 템플릿의 권한을 받는다. |
| Role-02 | 0 이하 Space ID, 미정의 시스템 키 또는 UTC 오프셋이 아닌 시각으로 생성한다. | 각각 `SPACE_ROLE_SPACE_ID_INVALID`, `SPACE_ROLE_SYSTEM_KEY_INVALID`, `SPACE_ROLE_INVALID_STATE`를 반환한다. |
| Role-03 | 저장된 식별자·표시 정보·시각과 권한을 복원한다. ADMIN의 저장 권한이 비거나 일부만 있는 경우, 비 ADMIN의 빈 집합·중복 권한도 확인한다. | 저장값을 보존한다. ADMIN은 정의된 전체 권한을 노출하고 비 ADMIN은 저장된 권한만 중복 없이 사용한다. |
| Role-04 | 복원에 사용한 권한 컬렉션을 바꾸거나 복원된 권한 집합을 수정한다. | 복원 시 원본을 복사하며 반환한 권한 집합은 수정할 수 없다. |
| Role-05 | 잘못된 스냅샷·색상·미정의 시스템 키·미정의 권한 또는 `null` 권한 컬렉션으로 복원한다. | 잘못된 저장값은 인수 예외로 거부한다. |

## Permission 코드

[SpacePermissionCodesTests](../../../tests/CloudSharp.Core.Tests/Domain/Memberships/SpacePermissionCodesTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| Permission-01 | 정의된 모든 `SpacePermission`의 문자열 코드를 조회한다. | 각 값은 `SPACE_VIEW`, `MEMBERS_MANAGE` 등 계약에 정의된 고유 코드에 매핑된다. |
| Permission-02 | 기본값 0이나 미정의 enum 값으로 코드 조회를 시도한다. | 기본값은 권한으로 정의되어 있지 않으며 조회는 `ArgumentOutOfRangeException`으로 실패한다. |

## RL-01 Space Role 목록 조회

[ListSpaceRolesUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Memberships/RoleListings/ListSpaceRolesUseCaseTests.cs) · [SpaceMembershipScaffoldingTests](../../../tests/CloudSharp.Core.Tests/UseCases/Memberships/SpaceMembershipScaffoldingTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| RL-01-01 | 인가된 `ACTIVE`·`ARCHIVED` Space의 Role을 읽는다. ADMIN·MEMBER·VIEWER와 사용자 정의 Role 및 삭제된 Role을 준비한다. | 인가된 Space ID로 조회하고 활성 관리형 Role만 반환한다. DTO의 PublicId·시스템 코드·이름·`IsManaged`·Permission을 확인한다. |
| RL-01-02 | 인가 후 저장된 Space가 `DELETING`·`DELETED`로 바뀌어 목록을 요청한다. | `AUTH_SPACE_STATE_NOT_ALLOWED`를 반환하고 Role을 읽지 않는다. |
| RL-01-03 | Role 읽기 포트에서 실패를 반환한다. | `DEPENDENCY_UNAVAILABLE` 오류가 결과에 전달된다. |
| RL-01-04 | Query 자체가 `null`이거나 Query의 `Space`가 누락된다. | 각각 `ArgumentNullException`, `SPACE_AUTHORIZATION_CONTEXT_REQUIRED` / `Space`다. |

[Space Role 도메인 설계](../../../.llm/domains/space-role.md) · [공통 Space 인가 테스트](space-test-cases.md#공통-space-인가-정책)
