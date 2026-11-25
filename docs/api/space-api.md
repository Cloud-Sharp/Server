# Space API

`/api/v2/spaces`에 구현된 Space 유즈케이스 SP-01~SP-10의 현재 Session HTTP 계약이다. [Space·접근제어 설계](../../.llm/api/spaces-access.md) · [Space 유즈케이스](../use-cases/space-use-cases.md#1-space) · [Space 저장소](../repositories/space-repositories.md)

현재 10개 경로는 모두 `UserSessionOnly` 정책을 사용한다. Space 생성은 멱등 키 검증과 최초 응답 재생을 지원한다. MCP 조회, 전역 관리자 우회와 감사 기록, 삭제 완료 Worker는 후속 범위다.

`S`는 `/api/v2/spaces/{spacePublicId}`를 뜻한다.

| ID | API | 메서드·경로 | 인증·권한 | 허용 Space 상태 | 요청 | 성공 응답 |
|---|---|---|---|---|---|---|
| SP-01 | [접근 가능한 Space 목록 조회](#sp-01-접근-가능한-space-목록-조회) | `GET /api/v2/spaces` | Session | `ACTIVE`, `ARCHIVED` | cursor query | 200, `items`와 `nextCursor` |
| SP-02 | [Space 생성](#sp-02-space-생성) | `POST /api/v2/spaces` | Session | 신규 `ACTIVE` | `Idempotency-Key`, JSON | 201, Space, `ETag`, `Location` |
| SP-03 | [Space 상세 조회](#sp-03-space-상세-조회) | `GET S` | Session, `SPACE_VIEW` | `ACTIVE`, `ARCHIVED` | — | 200, Space, `ETag` |
| SP-04 | [Space 정보 수정](#sp-04-space-정보-수정) | `PATCH S` | Session, `SPACE_UPDATE` | `ACTIVE` | `If-Match`, JSON | 200, Space, `ETag` |
| SP-05 | [Space 보관](#sp-05-space-보관) | `POST S/archive` | Session, `SPACE_UPDATE` | `ACTIVE` | `If-Match` | 200, Space, `ETag` |
| SP-06 | [Space 재활성화](#sp-06-space-재활성화) | `POST S/reactivate` | Session, `SPACE_UPDATE` | `ARCHIVED` | `If-Match` | 200, Space, `ETag` |
| SP-07 | [Space 삭제 요청](#sp-07-space-삭제-요청) | `DELETE S` | Session, OWNER | `ACTIVE`, `ARCHIVED` | `If-Match` | 202, 삭제 접수 정보, `ETag` |
| SP-08 | [Space quota 조회](#sp-08-space-quota-조회) | `GET S/quota` | Session, `SPACE_VIEW` | `ACTIVE`, `ARCHIVED` | — | 200, quota, Space `ETag` |
| SP-09 | [Space quota 변경](#sp-09-space-quota-변경) | `PATCH S/quota` | Session, OWNER | `ACTIVE` | Space `If-Match`, JSON | 200, quota, Space `ETag` |
| SP-10 | [Space 소유권 이전](#sp-10-space-소유권-이전) | `POST S/ownership-transfers` | Session, OWNER | `ACTIVE` | `If-Match`, JSON | 200, Space, `ETag` |

## 공통 계약

- `Authorization: Bearer {accessToken}`으로 인증하며 현재 10개 경로 모두 `UserSessionOnly` 정책을 적용한다. MCP 조회와 `spaces:read` scope, credential allowlist 평가는 후속 범위다. [Auth API](auth-api.md) · [MCP scope](../../.llm/api/notifications-mcp.md)
- Session의 기본 접근 자격은 OWNER 또는 활성 Membership이다. 멤버의 유효 권한은 할당된 활성 Role의 권한 합집합이다. 현재 HTTP 경로에서는 `SYSTEM_ADMIN`도 전역 우회 없이 실제 OWNER·Membership으로 평가하므로, 소유하거나 멤버인 Space만 조회할 수 있고 Membership 권한 밖의 변경은 거부된다. OWNER 전용 작업은 실제 소유자인지 검증한다. 전역 관리자 우회와 우회 감사 기록은 후속 범위다. [권한 계약](../../contracts/permissions/README.md) · [인가 설계](../../.llm/auth/authorization.md)
- 경로와 JSON에서 리소스를 식별할 때 RFC variant UUIDv7 PublicId를 사용한다. JSON 필드는 `camelCase`, enum은 `UPPER_SNAKE_CASE`, 시각은 UTC ISO 8601이다. 성공·오류 응답에 `Cache-Control: no-store`를 적용한다. 오류 본문은 `requestId`와 `error` 객체를 반환한다. `requestId`는 `X-Correlation-ID`와 같으며, `error`의 필드는 `code`, `message`, `details`다. [공통 API 계약](../../.llm/api/common-contract.md)
- `If-Match`에는 `"v7"` 형식의 양수 버전을 가진 강한 ETag 하나를 사용한다. 상세와 quota 조회는 같은 Space Aggregate 버전을 사용한다. 실제 변경 성공 시 증가한 버전을 반환하며, 동일한 값으로 처리된 성공 응답은 현재 버전을 사용한다. [ETag 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)
- 단건 요청은 기본 접근 자격을 확인한 뒤 Space 상태와 작업 권한을 평가한다. 상태와 권한이 허용된 변경 요청에서 버전을 비교한다.

| HTTP | 오류 코드 | 조건 |
|---|---|---|
| 400 | `REQUEST_VALIDATION_FAILED` | 잘못된 UUIDv7, JSON·query 형식, 알 수 없는 JSON 필드, 필드 제약 위반 또는 잘못된 헤더. 약한 ETag·와일드카드·복수 ETag는 `ifMatch` / `INVALID_ETAG` |
| 400 | `CURSOR_INVALID` | 비어 있거나 위조됐거나 요청자·정렬 방향 범위가 다른 목록 cursor |
| 401 | `AUTH_TOKEN_REQUIRED`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED` | 각각 인증 누락, 유효하지 않은 인증, 허용되지 않는 credential 종류 |
| 403 | `PERMISSION_DENIED` | 기본 접근 자격이 있는 호출자의 관리 권한 또는 OWNER 조건 부족 |
| 404 | `RESOURCE_NOT_FOUND` | Space 미존재, 기본 접근 거부, `DELETING`·`DELETED`, 단건 조회 권한 부족. 동일한 코드·메시지·details를 반환한다. |
| 409 | `SPACE_ARCHIVED` | 기본 접근이 확인된 `ARCHIVED` Space에 메타데이터 수정·보관·quota 변경·소유권 이전 요청 |
| 409 | `SPACE_INVALID_STATE` | 재활성화 등 현재 상태에서 허용되지 않는 상태 전이 요청 |
| 409 | `IDEMPOTENCY_KEY_REUSED` | 같은 요청자 범위와 생성 키에 서로 다른 입력 |
| 409 | `IDEMPOTENCY_REQUEST_IN_PROGRESS` | 같은 생성 키와 입력의 첫 요청을 처리 중 |
| 412 | `PRECONDITION_FAILED` | 현재 버전과 다른 `If-Match` 또는 저장 시 낙관적 동시성 충돌 |
| 415 | `REQUEST_VALIDATION_FAILED` | JSON 요청의 미지원 미디어 타입 |
| 428 | `PRECONDITION_REQUIRED` | 필수 `If-Match` 누락 |
| 503 | `DEPENDENCY_UNAVAILABLE` | 인증, 인가, 처리에 필요한 저장소 가용성 장애 |

### Space 응답

단건 응답과 목록 항목은 같은 Space 객체를 사용한다. `description`은 `null`일 수 있다. `myAccess`는 응답의 Space에 대한 요청자의 실제 권한을 나타낸다. 현재 Session 경로의 `source`는 `OWNER` 또는 `MEMBERSHIP`이며, `SYSTEM_ADMIN`도 이 둘 중 하나로 평가한다. `permissions`에는 유효 권한의 공개 코드를 담는다. 버전은 단건 응답의 `ETag` 헤더로 제공한다. `SYSTEM_ADMIN_BYPASS`와 MCP의 `MCP_OWNER`·`MCP_MEMBERSHIP`은 후속 인가·인증 연결의 계약이다. [Space DTO](../../src/CloudSharp.Core/UseCases/Spaces/Dtos/SpaceDto.cs) · [권한 코드](../../contracts/permissions/README.md#permission-catalog)

```json
{
  "spacePublicId": "01977b58-32aa-7c60-a17e-0840c6df1200",
  "ownerUserPublicId": "01977b58-7dcb-78a2-bf2b-f5aad30ec001",
  "name": "Product",
  "description": null,
  "status": "ACTIVE",
  "myAccess": {
    "source": "MEMBERSHIP",
    "permissions": ["SPACE_VIEW", "FILES_READ", "FILES_DOWNLOAD"]
  },
  "createdAt": "2026-06-22T12:00:00Z",
  "updatedAt": "2026-06-22T12:00:00Z"
}
```

## SP-01 접근 가능한 Space 목록 조회

- `cursor`는 첫 페이지에서 생략하고 다음 페이지부터 직전 `nextCursor` 값을 사용한다. `limit`은 1~100인 정수이며 기본값은 50이다. `sort`는 `createdAt`이 유일한 값이며 기본값이다. `direction`은 `ASC` 또는 `DESC`이며 기본값은 `DESC`다. 생성 시각이 같으면 Space PublicId에 같은 정렬 방향을 적용한다. [목록 저장소](../../src/CloudSharp.Infrastructure/Persistence/Repositories/SpaceListCandidateReader.cs)
- 인가된 Space로 `items`를 채운다. 더 조회할 수 있는 항목이 있으면 마지막 반환 항목의 서명된 base64url cursor를 `nextCursor`로 반환한다. 마지막 페이지는 `nextCursor: null`, 접근 가능한 항목이 없으면 `items: []`다.
- cursor는 요청자 범위, 정렬 필드, 방향에 묶는다. 비어 있거나 위조됐거나 범위가 다른 cursor는 400 `CURSOR_INVALID`다. 유효하지 않은 `limit`·`sort`·`direction`은 400 `REQUEST_VALIDATION_FAILED`다. 후속 MCP cursor의 요청자 범위에는 credential도 포함한다. [API 계약 검증](../../.llm/api/validation.md#3-cursor)

## SP-02 Space 생성

- `name`은 필수 문자열이며 양끝 공백을 제거한 뒤 1~200자다. `description`은 선택 문자열 또는 `null`이며, 양끝 공백을 제거한 뒤 빈 문자열이면 `null`로 저장한다. `allowedBytes`는 선택 필드이며 0 이상의 64비트 정수 또는 `null`을 받는다. 생략과 `null`은 모두 무제한이다. [생성 명령](../../src/CloudSharp.Core/UseCases/Spaces/Creations/CreateSpaceCommand.cs)
- 요청자를 OWNER로 지정하고 사용량·예약량을 0으로 초기화한다. Space와 관리형 ADMIN·MEMBER·VIEWER Role을 함께 커밋한다. 201 응답은 생성한 Space와 `ETag`, 상대 경로 `Location: /api/v2/spaces/{spacePublicId}`를 반환한다.
- 출력 가능한 ASCII 문자로 구성된 16~128자 `Idempotency-Key` 값 하나가 필요하다. 헤더 누락·복수 값·형식 오류는 400 `REQUEST_VALIDATION_FAILED`를 반환하며 오류 세부 항목은 `idempotencyKey` / `INVALID_IDEMPOTENCY_KEY`다.
- 같은 요청자 범위에서 키와 정규화한 입력이 일치하면 완료 후 24시간 동안 최초 201 본문, `Location`, `ETag`를 재생한다. 그사이 Space를 수정해도 재생 결과는 생성 시점의 값을 사용한다. 이름·설명의 양끝 공백, 빈 설명과 `null`, 허용 용량의 생략과 `null`은 각 생성 규칙에 따라 정규화한다. 같은 키의 다른 입력이나 처리 중 요청은 위 409 오류를 반환한다. [공통 멱등성 설계](http-idempotency-filter-design.md)

## SP-03 Space 상세 조회

- Space 객체와 해당 스냅샷의 `ETag`를 반환한다. `myAccess`는 요청마다 평가한다. 콘텐츠 탐색은 Space의 Mount 목록에서 시작한다. [상세 조회](../../src/CloudSharp.Core/UseCases/Spaces/Details/GetSpaceUseCase.cs) · [Mount API 설계](../../.llm/api/mounts.md)

## SP-04 Space 정보 수정

- JSON 객체에서 `name`과 `description`을 부분 변경한다. 생략한 필드는 현재 값을 사용한다. `name`을 제출할 때는 생성과 같은 문자열 제약을 적용하며 `null`은 400이다. `description: null` 또는 공백 문자열은 설명을 지운다.
- 빈 객체이거나 정규화 결과가 현재 값과 같은 요청은 유효한 `If-Match`를 확인한 뒤 현재 Space, `updatedAt`, `ETag`를 반환한다. [수정 명령](../../src/CloudSharp.Core/UseCases/Spaces/MetadataUpdates/UpdateSpaceCommand.cs)

## SP-05 Space 보관

- 본문 없이 호출한다. `ACTIVE`에서 `ARCHIVED`로 전환한 Space와 갱신된 `ETag`를 반환한다. 보관 후에도 상세와 quota를 읽을 수 있다. 다시 보관을 요청하면 409 `SPACE_ARCHIVED`다.

## SP-06 Space 재활성화

- 본문 없이 호출한다. `ARCHIVED`에서 `ACTIVE`로 전환한 Space와 갱신된 `ETag`를 반환한다. 이미 `ACTIVE`인 Space를 재활성화하면 409 `SPACE_INVALID_STATE`다.

## SP-07 Space 삭제 요청

- 본문 없이 호출한다. `ACTIVE` 또는 `ARCHIVED`를 `DELETING`으로 전환한다. 202 응답 본문은 `spacePublicId`, `status: "DELETING"`, `acceptedAt`을 담고 헤더에는 전환 시점의 `ETag`를 반환한다. 접수 직후 일반 Space 조회와 반복 삭제 요청은 404다.
- 후속 삭제 Worker가 종속 리소스를 정리하고 사용량과 예약량이 모두 0일 때 `DELETED`로 전환한다. 현재 범위는 삭제 접수와 `DELETING` 전이까지다. [삭제 접수 DTO](../../src/CloudSharp.Core/UseCases/Spaces/Dtos/SpaceDeletionAcceptedDto.cs) · [Space 상태 전이](../../.llm/domains/space.md)

## SP-08 Space quota 조회

- 응답 필드는 `spacePublicId`, `allowedBytes`, `usedBytes`, `reservedBytes`, `availableBytes`다. 용량은 바이트 단위 64비트 정수이며 `availableBytes`는 `allowedBytes - usedBytes - reservedBytes`다. 무제한이면 `allowedBytes`와 `availableBytes`는 모두 `null`이다. [Quota DTO](../../src/CloudSharp.Core/UseCases/Spaces/Dtos/SpaceQuotaDto.cs)

## SP-09 Space quota 변경

- JSON에 `allowedBytes`가 있어야 한다. 0 이상의 64비트 정수를 받으며, 명시적 `null`은 무제한으로 변경한다. 현재 사용량과 예약량의 합보다 작은 유한 한도는 409 `SPACE_QUOTA_TOO_SMALL`이다. 200 응답은 변경 결과의 quota와 Space `ETag`를 반환한다. [Quota 변경 명령](../../src/CloudSharp.Core/UseCases/Spaces/QuotaChanges/ChangeSpaceQuotaCommand.cs)

## SP-10 Space 소유권 이전

- JSON에 `newOwnerUserPublicId` UUIDv7을 사용한다. 대상은 같은 Space의 활성 Membership을 가진 활성 계정이어야 한다. 부적격 대상은 동일하게 409 `SPACE_TRANSFER_TARGET_INVALID`다. 현재 OWNER를 지정하면 유효한 `If-Match`를 확인한 뒤 현재 Space와 `ETag`로 200을 반환한다.
- Space의 소유자 변경, 새 OWNER의 Membership·Role 연결 제거, 이전 OWNER의 ADMIN Membership 생성을 하나의 트랜잭션으로 처리한다. 200 응답의 `ownerUserPublicId`는 새 OWNER이며 `myAccess`는 요청자인 이전 OWNER의 이전 완료 후 권한을 담는다. 현재 HTTP 경로에서는 이전 OWNER가 `SYSTEM_ADMIN`이어도 `myAccess.source`는 `MEMBERSHIP`이다. [소유권 이전 유즈케이스](../../src/CloudSharp.Core/UseCases/Spaces/OwnershipTransfers/TransferSpaceOwnershipUseCase.cs)

## 현재 구현과 후속 계약

- API는 `ICurrentActorAccessor`로 인증된 요청자를 얻는다. 단건 경로의 공통 endpoint filter가 UUIDv7을 검증하고 `ISpaceAuthorizationService`를 호출한 뒤 인가 결과를 기존 유즈케이스에 전달한다. 인가는 handler의 `If-Match` 처리보다 먼저 실행되며, Core 인가 오류는 위 상태·권한·마스킹 규칙으로 매핑한다. 버전은 `ETagFormatter`로 헤더에 기록한다. [ASP.NET 인가 설계](../../.llm/auth/aspnet-filters.md) · [Space 엔드포인트](../../src/CloudSharp.Api/Endpoints/Spaces/SpaceEndpoints.cs)
- SP-01~SP-10의 Session 경로와 SP-07~SP-09의 DI 생성자 연결을 구현했다. SP-02는 `CreateSpaceUseCase`의 멱등 실행 경로를 호출해 PostgreSQL에서 Space와 관리형 Role, 재생 결과를 같은 트랜잭션에 커밋한다. [API 라우트 등록](../../src/CloudSharp.Api/Program.cs) · [Space DI 등록](../../src/CloudSharp.Api/DependencyInjection/SpaceServiceCollectionExtensions.cs)
- Outbox 감사, 전역 관리자 우회, MCP 조회, 삭제 완료 Worker는 후속 범위다. 후속 목표 계약은 [공통 API 계약](../../.llm/api/common-contract.md), [Space·접근제어 설계](../../.llm/api/spaces-access.md), [인가 설계](../../.llm/auth/authorization.md), [Space 상태 전이](../../.llm/domains/space.md)를 따른다.
