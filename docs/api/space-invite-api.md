# Space Invite API

Space 초대의 관리, 공개 정보 조회, 수락에 대한 HTTP 계약이다. IN-01~IN-05는 구현된 유즈케이스에 대응하며 IN-06은 폐기에 필요한 최신 ETag를 제공하는 추가 조회 계약이다. [Space·접근제어 설계](../../.llm/api/spaces-access.md) · [Invite 유즈케이스](../use-cases/space-use-cases.md#4-space-invite) · [Invite 저장소](../repositories/space-invite-repositories.md)

IN-02·IN-05는 `Idempotency-Key`를 필수로 받는다.

`S`는 `/api/v2/spaces/{spacePublicId}`, `I`는 `S/invites/{invitePublicId}`다.

| ID    | API                                 | 메서드·경로                                      | 인증·권한                     | 허용 Space 상태          | 요청                      | 성공 응답                                 | 멱등성 종류 | `SYSTEM_ADMIN` 우회 |
| ----- | ----------------------------------- | ------------------------------------------- | ------------------------- | -------------------- | ----------------------- | ------------------------------------- | ----- | ----- |
| IN-01 | [초대 목록 조회](#in-01-초대-목록-조회)         | `GET S/invites`                             | Session, `INVITES_MANAGE` | `ACTIVE`, `ARCHIVED` | cursor query            | 200, `items`와 `nextCursor`            | 조회 | 없음 |
| IN-02 | [초대 링크 생성](#in-02-초대-링크-생성)         | `POST S/invites`                            | Session, `INVITES_MANAGE` | `ACTIVE`             | `Idempotency-Key`, JSON | 201, 발급 정보, Invite `ETag`, `Location` | 키 기반 (`Idempotency-Key` 필수) | 없음 |
| IN-03 | [초대 폐기](#in-03-초대-폐기)               | `DELETE I`                                  | Session, `INVITES_MANAGE` | `ACTIVE`             | Invite `If-Match`       | 204, 새 Invite `ETag`                  | 버전 비교 (`If-Match`) | 없음 |
| IN-04 | [공개 초대 정보 조회](#in-04-공개-초대-정보-조회)   | `GET /public/v2/invites/{inviteToken}`      | 유효한 초대 토큰, 익명 가능          | `ACTIVE`, `ARCHIVED` | —                       | 200, Space 이름·Role 이름·만료 시각           | 조회 | 없음 |
| IN-05 | [초대 수락](#in-05-초대-수락)               | `POST /api/v2/invites/{inviteToken}/accept` | Session, 유효한 초대 토큰        | `ACTIVE`             | `Idempotency-Key`       | 200, 가입 결과                            | 키 기반 (`Idempotency-Key` 필수) | 없음 |
| IN-06 | [관리용 초대 상세 조회](#in-06-관리용-초대-상세-조회) | `GET I`                                     | Session, `INVITES_MANAGE` | `ACTIVE`, `ARCHIVED` | —                       | 200, 초대 관리 정보, Invite `ETag`          | 조회 | 없음 |

## 공통 계약

- 관리 요청에는 `UserSessionOnly`와 `INVITES_MANAGE`를 적용한다. `SYSTEM_ADMIN` 요청에도 같은 Space 접근 자격, 상태, 관리 권한을 확인한다. 수락은 `UserSessionOnly`와 초대 토큰으로 인가하며 신규 사용자도 호출할 수 있다. 관리자 우회와 영속 감사 연결은 후속 범위다. [Space API](space-api.md#공통-계약) · [권한 계약](../../contracts/permissions/README.md#space접근-제어-endpoint)
- 관리 경로의 `spacePublicId`와 `invitePublicId`는 RFC variant UUIDv7이다. JSON 필드는 `camelCase`, 상태는 `UPPER_SNAKE_CASE`, 시각은 UTC ISO 8601을 사용한다. 오류 본문은 공통 `requestId`·`error` 형식이다. `requestId`는 응답의 `X-Correlation-ID`와 같다. [공통 API 계약](../../.llm/api/common-contract.md)
- 모든 응답에 `Cache-Control: no-store`를 적용한다. 토큰 경로인 IN-04·IN-05 응답에는 `Referrer-Policy: no-referrer`도 적용한다. 토큰은 대소문자를 구분하는 `cs_inv_` 접두사와 32바이트를 인코딩한 43자 base64url 본문으로 구성된다. nginx와 API 접근 로그는 토큰 경로 구간을 마스킹하고 trace·metric에는 라우트 템플릿을 사용한다. [토큰 형식 검증](../../src/CloudSharp.Core/UseCases/Invites/InviteTokenValidationRules.cs) · [토큰 경로 보호](../../.llm/api/common-contract.md#11-capability-token-경로-보호)
- Invite `If-Match`에는 IN-02 또는 IN-06 응답에서 받은 `"v7"` 형식의 강한 ETag 하나를 사용한다. 버전은 초대 Aggregate의 버전이다. 약한 ETag, 와일드카드, 복수 값은 400 `REQUEST_VALIDATION_FAILED` (`ifMatch` / `INVALID_ETAG`)다. [ETag 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)

| HTTP | 오류 코드 | 조건 |
|---|---|---|
| 400 | `REQUEST_VALIDATION_FAILED` | 잘못된 관리 경로 PublicId, JSON·query 형식, 알 수 없는 JSON 필드, 필드 제약 위반 또는 잘못된 헤더 |
| 401 | `AUTH_TOKEN_REQUIRED`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED` | Session 인증 누락, 유효하지 않은 Session·계정, 허용되지 않는 credential 종류 |
| 403 | `PERMISSION_DENIED` | 기본 접근 자격이 있는 호출자의 초대 관리 권한 부족 |
| 404 | `RESOURCE_NOT_FOUND` | 미존재·기본 접근 거부·삭제 중 또는 삭제된 Space, 미존재 또는 다른 Space의 초대, 공개 조회·수락에서 사용할 수 없는 capability. 동일한 코드·메시지·details를 반환한다. |
| 409 | `SPACE_ARCHIVED` | `ARCHIVED` Space에서 초대 생성·폐기 또는 유효한 초대 수락을 시도함 |
| 412 | `PRECONDITION_FAILED` | IN-03의 `If-Match` 버전 불일치 또는 폐기 저장 경합 |
| 415 | `REQUEST_VALIDATION_FAILED` | 생성 JSON의 미지원 미디어 타입 |
| 428 | `PRECONDITION_REQUIRED` | IN-03의 `If-Match` 누락 |
| 503 | `DEPENDENCY_UNAVAILABLE` | 인증·인가·초대 처리에 필요한 저장소 가용성 장애 |

## IN-01 초대 목록 조회

- 같은 Space에서 저장 상태가 `ACTIVE`인 초대만 조회한다. 만료 시각이 지났어도 저장 상태가 `ACTIVE`면 목록에 포함한다. 실제 가입 가능 여부는 현재 시각과 사용 한도를 적용해 공개 조회·수락 시 판정한다. [목록 저장소](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceInviteReader.cs)
- `limit`은 1~100, 기본 50이다. 유일한 `sort`는 대소문자를 구분하지 않는 `createdAt`이며 생략 시 기본값으로 적용한다. 생성 시각이 같으면 Invite PublicId를 비교한다. `direction`은 두 정렬 키에 같은 방향을 적용하며 `ASC` 또는 `DESC`를 받는다. 기본은 `DESC`다.
- 첫 페이지는 `cursor`를 생략한다. 후속 요청은 직전 응답의 서명된 base64url `nextCursor`를 그대로 사용한다. 응답은 `items`와 `nextCursor`를 담으며, 빈 페이지는 `items: []`, 마지막 페이지는 `nextCursor: null`을 반환한다. 다른 Space, 행위자, 조회 범위 또는 정렬에 발급된 cursor와 위조·빈 cursor는 400 `CURSOR_INVALID`다. [Cursor 검증 기준](../../.llm/api/validation.md#3-cursor)
- 각 항목은 `invitePublicId`, `status`, `expiresAt`, `maxUses`, `useCount`, `createdAt`, `updatedAt`, `revokedAt`을 담는다. `maxUses: null`은 무제한이며 이 목록의 `revokedAt`은 항상 `null`이다. 폐기 전에는 관리용 상세를 조회해 해당 초대의 ETag를 받는다. [관리 응답 DTO](../../src/CloudSharp.Core/UseCases/Invites/Dtos/SpaceInviteDto.cs)

## IN-02 초대 링크 생성

- JSON의 `expiresAt`은 현재보다 뒤인 UTC 시각으로 필수다. `maxUses`는 선택 필드이며 1~2,147,483,647의 정수 또는 `null`을 받는다. 생략과 `null`은 모두 무제한이다. 초기 상태는 `ACTIVE`, 사용 횟수는 0이다. 초대 수락으로 부여하는 Role은 VIEWER로 고정한다. 데이터베이스에는 토큰 해시를 보관한다. [생성 명령](../../src/CloudSharp.Core/UseCases/Invites/Creations/CreateSpaceInviteCommand.cs) · [Invite 도메인](../../.llm/domains/space-invite.md)
- 최초 201 본문은 `invitePublicId`, `inviteToken`, `inviteUrl`, `expiresAt`, `maxUses`를 담는다. `inviteUrl`은 `/public/v2/invites/{inviteToken}`의 상대 경로다. `Location`에는 관리용 상세 경로 `I`를 사용하며 버전은 Invite `ETag`로 반환한다. 토큰과 URL은 이 최초 성공 본문에서 한 번 제공한다. [발급 응답 DTO](../../src/CloudSharp.Core/UseCases/Invites/Dtos/CreateSpaceInviteResultDto.cs)
- 발급 응답을 받지 못하면 같은 키와 입력으로 재시도한다. 이미 발급이 완료됐다면 409 `SECRET_RESPONSE_NOT_REPLAYABLE`과 최초 `invitePublicId`를 응답 본문 최상위에 반환한다. 이 ID로 관리용 상세를 조회하고 폐기할 수 있다.

## IN-03 초대 폐기

- 본문 없이 초대의 최신 `If-Match`를 보낸다. 조회한 버전이 일치하고 저장 상태가 `ACTIVE`인 초대를 `REVOKED`로 전환한다. 시각상 만료된 초대도 저장 상태가 `ACTIVE`면 폐기할 수 있다. 저장 상태가 `REVOKED`·`EXPIRED`이고 버전이 일치하면 409 `INVITE_INVALID_STATE`다.
- 성공은 본문 없는 204와 새 `ETag`다. 폐기는 이후 수락을 막으며, 기존 가입자는 Membership 관리 API로 관리한다. 412를 받으면 IN-06에서 현재 상태와 ETag를 다시 확인할 수 있다. [폐기 유즈케이스](../../src/CloudSharp.Core/UseCases/Invites/Revocations/RevokeSpaceInviteUseCase.cs)

## IN-04 공개 초대 정보 조회

- URL 경로의 토큰으로 조회한다. 토큰이 사용 가능하고 대상 Space가 `ACTIVE` 또는 `ARCHIVED`이며 그 Space에 관리형 활성 VIEWER Role이 정확히 하나 있으면 200을 반환한다. 본문은 `spaceName`, VIEWER Role 이름인 `roleName`, `expiresAt`만 담는다. 조회는 가입 슬롯을 소비하지 않는다.
- 형식 오류, 미등록, 폐기, 사용 한도 소진, 만료 시각 도달, 대상 Space 미존재·삭제 상태, VIEWER 조건 불충족은 동일한 404 `RESOURCE_NOT_FOUND`다. [공개 조회 유즈케이스](../../src/CloudSharp.Core/UseCases/Invites/PublicInfo/GetPublicSpaceInviteUseCase.cs)

## IN-05 초대 수락

- 활성 계정의 Session으로 본문 없이 호출하며 `Idempotency-Key`를 보낸다. 신규·재가입에서는 같은 Space의 활성 VIEWER Role로 Membership을 만든다. Membership·Role 저장과 초대 사용 횟수 증가는 한 트랜잭션에 커밋한다. 마지막 슬롯을 사용한 요청도 200으로 완료되며 초대는 `EXPIRED`가 된다. [수락 유즈케이스](../../src/CloudSharp.Core/UseCases/Invites/Acceptances/AcceptSpaceInviteUseCase.cs) · [Invite 상태 전이](../../.llm/domains/space-invite.md#상태-전이)
- 성공은 `spacePublicId`, `membershipPublicId`, `alreadyMember`를 모두 반환한다. 첫 실행은 초대의 사용 가능 여부를 검증한 뒤 가입 관계를 평가한다.

| 요청자의 가입 관계 | `membershipPublicId` | `alreadyMember` |
|---|---|---|
| 미가입 또는 최신 Membership이 `LEFT` | 새 Membership의 PublicId | `false` |
| `ACTIVE` Membership | 기존 Membership의 PublicId | `true` |
| OWNER | `null` | `true` |

- 기존 OWNER·활성 멤버의 성공은 가입 슬롯을 소비하지 않는다. `SUSPENDED` 사용자가 유효한 초대를 수락하면 409 `MEMBER_INVALID_STATE`를 반환한다.
- 형식 오류·미등록·폐기·한도 소진·만료 토큰과 미존재·삭제 상태의 Space는 동일한 404 `RESOURCE_NOT_FOUND`다. 신규 가입에 필요한 VIEWER Role을 확인할 수 없는 경우도 404다. 사용 가능한 토큰이 `ARCHIVED` Space를 가리키면 409 `SPACE_ARCHIVED`다. 이 판정은 새 키로 수락하는 기존 참여자에게도 적용한다.
- 같은 키와 입력으로 완료된 수락을 다시 요청하면 최초 200 본문을 재생한다. 최초 요청이 마지막 슬롯을 사용했을 때도 최초 가입 결과가 반환된다. 새 키로 동시 수락해 저장 충돌이 생기면 최신 상태를 평가해 가입·사용 횟수 중복을 막고, 경합을 해소하지 못하면 409 `REQUEST_CONFLICT`를 반환한다.

## IN-06 관리용 초대 상세 조회

- IN-03과 같은 경로를 `GET`으로 호출한다. 요청한 Space에 속한 초대의 현재 저장 상태와 IN-01의 항목 필드, 같은 스냅샷의 `ETag`를 반환한다. `ACTIVE`·`REVOKED`·`EXPIRED` 초대를 모두 조회할 수 있으며 `revokedAt`은 폐기된 초대에만 시각 값을 가진다.
- 초대가 없거나 다른 Space에 속하면 404 `RESOURCE_NOT_FOUND`다. 관리용 조회는 기존 Space 범위 `FindByPublicIdAsync`를 사용한다. [Invite Repository 계약](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceInviteRepository.cs)

## 생성·수락의 멱등 키

- 초대 생성 또는 수락을 처음 요청할 때 UUID 같은 새 키를 만들고 `Idempotency-Key` 헤더 하나로 보낸다. 출력 가능한 ASCII 16~128자를 사용한다. 헤더 누락·복수 값·형식 오류는 400 `REQUEST_VALIDATION_FAILED`와 `idempotencyKey` / `INVALID_IDEMPOTENCY_KEY`를 반환한다. [Idempotency-Key 사용 방법](../../src/CloudSharp.Api/README.md#idempotency-key)
- 같은 작업을 다시 시도할 때는 같은 사용자·경로·입력·키를 사용한다. 완료 결과는 24시간 동안 확인할 수 있다. 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS`를 받으면 같은 키와 입력으로 다시 시도할 수 있다. 같은 키에 다른 입력을 보내면 409 `IDEMPOTENCY_KEY_REUSED`를 반환한다.
- 재시도에도 유효한 Session을 보낸다. 초대 생성은 현재 `INVITES_MANAGE` 권한이 있으면 Space 보관 후에도 완료된 발급 결과를 확인할 수 있다. 초대 수락은 같은 경로와 키로 재시도하면 최초 200 본문이 반환되며, 최초 요청이 마지막 가입 슬롯을 사용한 경우에도 결과가 같다.
- 멱등성 판정에 필요한 저장소에 접근할 수 없으면 503 `DEPENDENCY_UNAVAILABLE`을 반환한다. 복구 후 같은 키와 입력으로 다시 시도할 수 있다.

## 엔드포인트 구현 시 연결할 계약

- IN-01~IN-03·IN-06은 `ISpaceAuthorizationService`에서 관리 권한을 확인한다. IN-04에는 익명 정책을 적용하고 IN-05에는 세션 인증을 적용한다. IN-05에서 사용할 수 없는 토큰이 보관 상태를 드러내지 않도록 초대 유효성을 확인한 뒤 `SPACE_ARCHIVED`를 매핑한다. 수락 저장에서 발생한 초대 버전·활성 Membership 등록 경합은 롤백 후 새 트랜잭션에서 최신 상태를 평가한다. [인가 필터 설계](../../.llm/auth/aspnet-filters.md)
- IN-01 유즈케이스는 cursor가 현재 행위자에게 발급됐는지 확인한다. Invite 목록 저장소는 전달받은 저장소 cursor의 Space·조회 상태·정렬·방향을 검증한다. IN-06은 저장소의 단건 조회 결과에서 별도 관리 응답과 ETag를 생성한다. [목록 유즈케이스](../../src/CloudSharp.Core/UseCases/Invites/Listings/ListSpaceInvitesUseCase.cs) · [Invite 목록 저장소](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceInviteReader.cs)
- IN-01~IN-04는 포트를 받는 생성자가 `internal`이다. 라우트를 연결할 때 DI가 필요한 포트를 공급하는 생성자를 사용하도록 등록을 맞춘다. [Invite DI 등록](../../src/CloudSharp.Api/DependencyInjection/InviteServiceCollectionExtensions.cs) · [Invite 테스트 케이스](../tests/core/space-invite-test-cases.md)
