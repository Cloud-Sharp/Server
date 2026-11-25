# Space Membership API

Space 멤버 목록·상세 조회, Role 할당, 상태 변경과 본인 탈퇴에 대한 MB-01~MB-07의 HTTP 계약이다. [Space·접근제어 설계](../../.llm/api/spaces-access.md) · [Membership 유즈케이스](../use-cases/space-use-cases.md#3-space-membership) · [Membership 저장소](../repositories/space-membership-repositories.md)

`S`는 `/api/v2/spaces/{spacePublicId}`, `M`은 `S/memberships/{membershipPublicId}`다.

| ID    | API                                   | 메서드·경로                | 인증·권한                      | 허용 Space 상태          | 요청                          | 성공 응답                      |
| ----- | ------------------------------------- | --------------------- | -------------------------- | -------------------- | --------------------------- | -------------------------- |
| MB-01 | [멤버 목록 조회](#mb-01-멤버-목록-조회)           | `GET S/memberships`   | Session, `MEMBERS_VIEW`    | `ACTIVE`, `ARCHIVED` | cursor query                | 200, `items`와 `nextCursor` |
| MB-02 | [멤버 상세 조회](#mb-02-멤버-상세-조회)           | `GET M`               | Session, `MEMBERS_VIEW`    | `ACTIVE`, `ARCHIVED` | —                           | 200, Membership과 `ETag`    |
| MB-03 | [멤버 Role 전체 교체](#mb-03-멤버-role-전체-교체) | `PUT M/roles`         | Session, `MEMBERS_MANAGE`  | `ACTIVE`             | Membership `If-Match`, JSON | 200, Membership과 `ETag`    |
| MB-04 | [멤버 정지](#mb-04-멤버-정지)                 | `POST M/suspend`      | Session, `MEMBERS_MANAGE`  | `ACTIVE`             | Membership `If-Match`       | 200, Membership과 `ETag`    |
| MB-05 | [정지 멤버 활성화](#mb-05-정지-멤버-활성화)         | `POST M/activate`     | Session, `MEMBERS_MANAGE`  | `ACTIVE`             | Membership `If-Match`       | 200, Membership과 `ETag`    |
| MB-06 | [멤버 제거](#mb-06-멤버-제거)                 | `DELETE M`            | Session, `MEMBERS_MANAGE`  | `ACTIVE`             | Membership `If-Match`       | 204, 새 Membership `ETag`   |
| MB-07 | [본인 탈퇴](#mb-07-본인-탈퇴)                 | `DELETE S/membership` | Session, 본인의 Membership 관계 | `ACTIVE`, `ARCHIVED` | 본문 없는 멱등 호출                 | 204                        |

## 공통 계약

- 모든 요청에 `UserSessionOnly`를 적용한다. API에서 MB-01~MB-06과 Role 목록의 Space 접근 자격, 상태, 작업 권한을 확인한다. 영속 감사가 연결되기 전까지 `SYSTEM_ADMIN`도 OWNER 또는 활성 Membership의 권한으로 평가한다. MB-07은 인증된 요청자와 Membership의 관계를 직접 확인한다. [Space API](space-api.md#공통-계약) · [권한 계약](../../contracts/permissions/README.md) · [인가 감사 계약](../../.llm/auth/authorization.md#11-auditevent)
- 경로와 JSON의 리소스 식별자는 RFC variant UUIDv7 PublicId다. JSON 필드는 `camelCase`, 상태는 `UPPER_SNAKE_CASE`, 시각은 UTC ISO 8601을 사용한다. 성공·오류 응답에 `Cache-Control: no-store`를 적용한다. 오류 본문은 공통 `requestId`·`error` 형식이며 `requestId`는 응답의 `X-Correlation-ID`와 같다. [공통 API 계약](../../.llm/api/common-contract.md)
- MB-03~MB-06의 `If-Match`에는 MB-02에서 받은 Membership `ETag`를 사용한다. `"v7"`처럼 양수 버전을 담은 강한 ETag 하나를 받는다. 대상이 확인되면 현재 버전을 비교한 뒤 상태 전이를 평가한다. 실제 변경 성공 시 새 ETag를 반환한다. [ETag 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)

| HTTP | 오류 코드 | 조건 |
|---|---|---|
| 400 | `REQUEST_VALIDATION_FAILED` | 잘못된 PublicId, JSON·query 형식, 알 수 없는 JSON 필드·필드 제약 위반 또는 잘못된 헤더. 약한 ETag·와일드카드·복수 ETag는 `ifMatch` / `INVALID_ETAG` |
| 401 | `AUTH_TOKEN_REQUIRED`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED` | Session 인증 누락, 유효하지 않은 Session·계정, 허용되지 않는 credential 종류 |
| 403 | `PERMISSION_DENIED` | 기본 접근 자격이 있는 호출자의 멤버 조회·관리 권한 부족 또는 OWNER의 본인 탈퇴 요청 |
| 404 | `RESOURCE_NOT_FOUND` | Space 미존재·기본 접근 거부·`DELETING`·`DELETED`, 같은 Space에서 찾을 수 없는 대상 Membership·Role, 현재 OWNER를 가리키는 멤버 조회·관리 요청, 또는 MB-07의 본인 관계 없음. 동일한 코드·메시지·details를 반환한다. |
| 409 | `SPACE_ARCHIVED` | 기본 접근이 확인된 `ARCHIVED` Space에서 MB-03~MB-06 요청 |
| 409 | `MEMBER_INACTIVE` | 최신 버전으로 `LEFT` Membership에 MB-03~MB-06 요청 |
| 412 | `PRECONDITION_FAILED` | MB-03~MB-06의 `If-Match` 불일치 또는 저장 시 동시성 충돌 |
| 415 | `REQUEST_VALIDATION_FAILED` | Role 교체 JSON의 미지원 미디어 타입 |
| 428 | `PRECONDITION_REQUIRED` | MB-03~MB-06의 `If-Match` 누락 |
| 503 | `DEPENDENCY_UNAVAILABLE` | 인증·인가·처리에 필요한 저장소 가용성 장애 |

### Membership 응답

목록과 단건은 같은 Membership 객체를 사용한다. `user.userName`과 `user.displayName`은 각각 `null`일 수 있다. `roles`는 할당된 Role의 요약이며 `SUSPENDED`·`LEFT`에서는 빈 배열일 수 있다. 할당 이력은 탈퇴 후에도 조회에 사용한다. 할당 가능한 관리형 Role과 각 Role의 유효 Permission은 Role 목록에서 조회한다. [Membership DTO](../../src/CloudSharp.Core/UseCases/Memberships/Dtos/SpaceMembershipDto.cs) · [Space Role API](space-role-api.md)

```json
{
  "membershipPublicId": "01977b5b-164c-73b8-b681-1eabcc05a444",
  "user": {
    "userPublicId": "01977b58-9ccc-7674-9554-3f85adeea222",
    "userName": "member",
    "displayName": null
  },
  "status": "ACTIVE",
  "roles": [
    {
      "rolePublicId": "01977b5a-a709-7470-a957-1c707dbbc333",
      "code": "VIEWER",
      "name": "Viewer"
    }
  ],
  "joinedAt": "2026-06-22T12:00:00Z",
  "updatedAt": "2026-06-22T12:00:00Z"
}
```

## MB-01 멤버 목록 조회

- 현재 OWNER를 제외한 같은 Space의 `ACTIVE`·`SUSPENDED`·`LEFT` Membership을 조회한다. 재가입한 사용자는 과거 `LEFT` 이력과 새 Membership이 별도 항목으로 나올 수 있다. 항목은 `membershipPublicId`로 식별한다. [목록 저장소](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceMembershipReader.cs)
- `limit`은 1~100인 정수이며 기본은 50이다. `sort`는 대소문자를 구분하지 않는 `joinedAt` 또는 `updatedAt`이며 기본은 `joinedAt`이다. `direction`은 `ASC` 또는 `DESC`이며 기본은 `DESC`다. 시간이 같으면 Membership PublicId에 같은 방향을 적용한다.
- 첫 페이지는 `cursor`를 생략한다. 후속 요청에 직전 응답의 서명된 base64url `nextCursor`를 그대로 보낸다. 빈 페이지는 `items: []`, 마지막 페이지는 `nextCursor: null`이다. cursor는 요청자, Space, 현재 OWNER를 포함한 조회 범위, 정렬과 방향에 묶인다. 비어 있거나 위조됐거나 범위가 다른 cursor는 400 `CURSOR_INVALID`다. [Cursor 검증 기준](../../.llm/api/validation.md#3-cursor)

## MB-02 멤버 상세 조회

- 같은 Space의 `ACTIVE`·`SUSPENDED`·`LEFT` Membership 객체와 같은 스냅샷의 `ETag`를 반환한다. 변경에서 412를 받으면 이 경로에서 현재 상태와 버전을 다시 확인할 수 있다. [상세 조회 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/Details/GetSpaceMembershipUseCase.cs)

## MB-03 멤버 Role 전체 교체

- `rolePublicIds`는 중복 없는 RFC variant UUIDv7을 하나 이상 담은 필수 배열이다. 배열 누락·`null`·비어 있음·중복·잘못된 PublicId는 400 `REQUEST_VALIDATION_FAILED` (`rolePublicIds` / `MEMBER_INVALID_ROLE`)다. 항목에는 같은 Space의 활성 관리형 Role PublicId를 사용한다. 미존재·다른 Space·삭제된 Role이나 관리형 목록에서 벗어난 Role은 동일한 404 `RESOURCE_NOT_FOUND`로 처리한다. [Role 목록 계약](space-role-api.md#rl-01-space-role-목록-조회) · [Role 교체 명령](../../src/CloudSharp.Core/UseCases/Memberships/RoleReplacements/ReplaceSpaceMemberRolesCommand.cs)
- `ACTIVE`·`SUSPENDED` 멤버의 Role 집합을 한 번에 교체한다. 유지한 Role의 부여 기록은 보존하고 새 Role에는 요청자와 부여 시각을 기록한다. 자기 자신의 Role도 `MEMBERS_MANAGE`가 있으면 교체할 수 있다.
- 최신 `If-Match`로 순서만 다른 같은 집합을 보내면 현재 Membership과 `ETag`로 200을 반환한다. Role 교체 결과의 `status`는 변경 전 Membership의 상태를 사용한다. [Role 교체 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/RoleReplacements/ReplaceSpaceMemberRolesUseCase.cs)

## MB-04 멤버 정지

- 본문 없이 호출한다. `ACTIVE`를 `SUSPENDED`로 바꾸고 Role 할당을 보존한다. 정지하면 해당 Membership을 통한 일반 Space 접근 자격이 해제된다. 새 Membership과 `ETag`로 200을 반환한다. 현재 버전의 `SUSPENDED` 멤버에 정지를 요청하면 409 `MEMBER_INVALID_STATE`다. [Membership 상태 전이](../../.llm/domains/space-membership.md#상태-전이)

## MB-05 정지 멤버 활성화

- 본문 없이 호출한다. Role 할당이 하나 이상 있는 `SUSPENDED` 멤버를 `ACTIVE`로 전환한다. 새 Membership과 `ETag`로 200을 반환한다. 현재 버전의 `ACTIVE` 멤버는 409 `MEMBER_INVALID_STATE`, Role 할당이 비어 있는 정지 멤버는 409 `MEMBER_INVALID_ROLE`이다. 비어 있는 경우 MB-03으로 Role을 부여한 뒤 활성화한다. [활성화 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/Activations/ActivateSpaceMemberUseCase.cs)

## MB-06 멤버 제거

- 본문 없이 호출한다. `ACTIVE`·`SUSPENDED` 멤버를 `LEFT`로 전환하고 Role 할당 이력을 보존한다. 성공은 본문 없는 204와 새 Membership `ETag`다. 이후 다시 가입하면 새 Membership이 생성된다. [제거 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/Removals/RemoveSpaceMemberUseCase.cs) · [초대 수락](space-invite-api.md#in-05-초대-수락)

## MB-07 본인 탈퇴

- 인증된 Session과 `spacePublicId`로 본인의 관계를 찾는다. `ACTIVE`·`ARCHIVED` Space의 본인 `ACTIVE`·`SUSPENDED` Membership을 `LEFT`로 전환하고 본문 없는 204를 반환한다. 현재 참여 관계를 우선하고, 최신 본인 기록이 이미 `LEFT`일 때도 204다. 실제 OWNER는 소유권 이전 후에 탈퇴할 수 있다. [본인 탈퇴 유즈케이스](../../src/CloudSharp.Core/UseCases/Memberships/Leaves/LeaveSpaceUseCase.cs) · [소유권 이전](space-api.md#sp-10-space-소유권-이전)
- 저장 경합이 발생하면 롤백 후 처음 대상이 된 Membership을 다시 확인한다. 같은 Membership이 이미 `LEFT`이면 204로 완료한다. 경합을 해결할 수 없으면 409 `REQUEST_CONFLICT`를 반환한다. 이 재확인은 처음 선택한 Membership에 한정한다.

## 엔드포인트 구현 시 연결할 계약

- MB-01~MB-06과 Role 목록은 `ISpaceAuthorizationService`의 인가 결과를 유즈케이스에 전달한다. MB-07에는 인증된 `CurrentActor`와 요청 Space를 전달해 별도 관계 정책을 적용한다. 변경 응답에 `ETagFormatter`를 재사용하고 Space·Membership 인가 실패를 공통 계약에 따라 매핑한다. 관리자 권한 우회와 실제 Role 변경의 영속 감사 연결은 후속 범위다. [인가 필터 설계](../../.llm/auth/aspnet-filters.md) · [Membership DI 등록](../../src/CloudSharp.Api/DependencyInjection/MembershipServiceCollectionExtensions.cs)
- 목록 cursor 서명에 요청자 범위를 추가한다. Role 교체에서 관리형 목록과 같은 Role 적격성을 검증하고, 배열 형식 오류와 Role 조회 실패의 외부 응답을 구분한다. MB-07의 저장 경합은 본인 탈퇴 계약에 따라 매핑한다. [Membership 테스트 케이스](../tests/core/space-membership-test-cases.md)
