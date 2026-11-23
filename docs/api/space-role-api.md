# Space Role API

Space 생성 시 등록되는 관리형 ADMIN·MEMBER·VIEWER Role의 조회 계약이다. [Space·접근제어 설계](../../.llm/api/spaces-access.md) · [Role 유즈케이스](../use-cases/space-use-cases.md#2-space-role) · [Role 저장소](../repositories/space-role-repositories.md)

| ID | API | 메서드·경로 | 인증·권한 | 허용 Space 상태 | 성공 응답 |
|---|---|---|---|---|---|
| RL-01 | [Space Role 목록 조회](#rl-01-space-role-목록-조회) | `GET /api/v2/spaces/{spacePublicId}/roles` | Session, `MEMBERS_VIEW` | `ACTIVE`, `ARCHIVED` | 200, Role 배열 |

## RL-01 Space Role 목록 조회

- 요청한 Space의 활성 관리형 Role을 조회하며, 결과는 Role 객체의 JSON 배열이다. 결과가 비어 있으면 `[]`를 반환한다. 클라이언트는 `rolePublicId`로 Role을 식별한다. [Role 목록 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/RoleListings/ListSpaceRolesUseCase.cs)
- 필드는 `rolePublicId` (UUIDv7), `code` (`ADMIN`, `MEMBER`, `VIEWER`), `name` (표시 이름), `isManaged` (`true`), `permissions` (유효 Permission 코드의 배열)이다. ADMIN은 현재 정의된 모든 Space Permission을 반환하고, MEMBER·VIEWER는 각 Role에 저장된 권한 집합을 반환한다. [Role DTO](../../src/CloudSharp.Core/UseCases/Memberships/Dtos/SpaceRoleDto.cs) · [Role 도메인](../../.llm/domains/space-role.md#permission-모델)
- Permission은 `SPACE_VIEW`, `MEMBERS_MANAGE` 같은 공개 코드로 직렬화한다. 각 코드의 의미와 기본 Role별 부여 규칙은 권한 계약을 따른다. OWNER 권한은 Space 소유 관계에서 계산한다. [공개 Permission 코드](../../src/CloudSharp.Core/Domain/Memberships/SpacePermissionCodes.cs) · [권한 계약](../../contracts/permissions/README.md#permission-catalog)
- 멤버에게 Role을 할당할 때는 같은 Space의 이 목록에서 받은 `rolePublicId`를 사용한다. 할당 변경에는 Membership 버전과 `MEMBERS_MANAGE`를 적용한다. [멤버 Role 전체 교체](space-membership-api.md#mb-03-멤버-role-전체-교체)

```json
[
  {
    "rolePublicId": "01977b5a-a709-7470-a957-1c707dbbc333",
    "code": "VIEWER",
    "name": "Viewer",
    "isManaged": true,
    "permissions": ["SPACE_VIEW", "FILES_READ", "FILES_DOWNLOAD"]
  }
]
```

## 인증과 오류

`Authorization: Bearer {accessToken}`으로 Session을 전달한다. `spacePublicId`는 RFC variant UUIDv7이다. 성공·오류 응답에 `Cache-Control: no-store`를 적용하며, 오류의 `requestId`와 응답의 `X-Correlation-ID`는 같다. 기본 접근 자격을 확인한 뒤 Space 상태와 `MEMBERS_VIEW`를 평가한다. Session `SYSTEM_ADMIN`이 권한을 우회한 요청은 감사 대상이다. [Space API 공통 계약](space-api.md#공통-계약) · [인가 감사 계약](../../.llm/auth/authorization.md#11-auditevent)

| HTTP | 오류 코드 | 조건 |
|---|---|---|
| 400 | `REQUEST_VALIDATION_FAILED` | 잘못된 `spacePublicId` |
| 401 | `AUTH_TOKEN_REQUIRED`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED` | Session 인증 누락, 유효하지 않은 Session·계정, 허용되지 않는 credential 종류 |
| 403 | `PERMISSION_DENIED` | 기본 접근 자격이 있는 호출자의 `MEMBERS_VIEW` 부족 |
| 404 | `RESOURCE_NOT_FOUND` | Space 미존재, 기본 접근 거부 또는 `DELETING`·`DELETED` 상태. 동일한 코드·메시지·details를 반환한다. |
| 503 | `DEPENDENCY_UNAVAILABLE` | 인증·인가·Role 조회에 필요한 저장소 가용성 장애 |

## 엔드포인트 구현 시 연결할 계약

`UserSessionOnly`와 Space 인가를 적용한 뒤 등록된 `ListSpaceRolesUseCase`를 호출한다. Role 시스템 키와 `SpacePermissionCodes.Get`을 사용해 공개 응답으로 변환한다. [Role DI 등록](../../src/CloudSharp.Api/DependencyInjection/MembershipServiceCollectionExtensions.cs) · [Role 테스트 케이스](../tests/core/space-role-test-cases.md)
