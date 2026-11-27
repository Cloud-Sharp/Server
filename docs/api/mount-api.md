# Mount API

Space 안의 Mount를 조회·개설하고, 설정·보관 상태와 Role·사용자별 권한 덮어쓰기를 관리하는 v2 HTTP 계약이다. MT-01~MT-10은 Space 하위 라우트에 등록되어 기존 Core 유즈케이스를 호출한다. [Mount 유즈케이스](../use-cases/mount-use-cases.md) · [Mount API 설계](../../.llm/api/mounts.md) · [유즈케이스 등록](../../src/CloudSharp.Api/DependencyInjection/MountServiceCollectionExtensions.cs) · [Mount 라우트 등록](../../src/CloudSharp.Api/Endpoints/Mounts/MountEndpoints.cs)

`S`는 `/api/v2/spaces/{spacePublicId}`, `M`은 `S/mounts/{mountPublicId}`다.

| ID    | API                                        | 메서드·경로                                   | 요구 권한                         | 요청                                        | 성공 응답                                        |
| ----- | ------------------------------------------ | ---------------------------------------- | ----------------------------- | ----------------------------------------- | -------------------------------------------- |
| MT-01 | [Mount 목록](#mt-01-mount-목록)                | `GET S/mounts`                           | Space 접근 자격, 항목별 `MOUNT_VIEW` | 페이지 query                                 | 200, `items`, `nextCursor`, `canCreateMount` |
| MT-02 | [Mount 개설](#mt-02-mount-개설)                | `POST S/mounts`                          | Space의 `MOUNT_MANAGE`         | `Idempotency-Key`, JSON                   | 201, 상세, `Location`, `ETag`                  |
| MT-03 | [Mount 상세](#mt-03-mount-상세)                | `GET M`                                  | `MOUNT_VIEW`                  | —                                         | 200, 상세, `ETag`                              |
| MT-04 | [Mount 설정 변경](#mt-04-mount-설정-변경)          | `PATCH M`                                | `MOUNT_MANAGE`                | `If-Match`, JSON                          | 200, 상세, `ETag`                              |
| MT-05 | [Mount 보관](#mt-05와-mt-06-mount-보관과-재활성화)   | `POST M/archive`                         | `MOUNT_MANAGE`                | `If-Match`                                | 200, 상세, `ETag`                              |
| MT-06 | [Mount 재활성화](#mt-05와-mt-06-mount-보관과-재활성화) | `POST M/reactivate`                      | `MOUNT_MANAGE`                | `If-Match`                                | 200, 상세, `ETag`                              |
| MT-07 | [권한 덮어쓰기 목록](#mt-07-권한-덮어쓰기-목록)            | `GET M/permissions`                      | `MOUNT_MANAGE`                | `targetType`, 페이지 query                   | 200, `items`, `nextCursor`                   |
| MT-08 | [Role 덮어쓰기 교체](#mt-08과-mt-09-권한-덮어쓰기-교체)   | `PUT M/permissions/roles/{rolePublicId}` | `MOUNT_MANAGE`와 바뀌는 권한        | `If-Match`, JSON                          | 200, 덮어쓰기 항목, `ETag`                         |
| MT-09 | [사용자 덮어쓰기 교체](#mt-08과-mt-09-권한-덮어쓰기-교체)    | `PUT M/permissions/users/{userPublicId}` | `MOUNT_MANAGE`와 바뀌는 권한        | `If-Match`, JSON                          | 200, 덮어쓰기 항목, `ETag`                         |
| MT-10 | [유효 권한 미리보기](#mt-10-유효-권한-미리보기)            | `GET M/permissions/preview`              | `MOUNT_MANAGE`                | `userPublicId` 또는 `visibility`, 페이지 query | 200, `items`, `nextCursor`                   |

## 공통 계약

- 모든 경로에 `Authorization: Bearer {accessToken}`으로 사용자 Session을 보낸다. 기본 Space 접근 자격은 활성 계정의 Space 소유자 또는 `ACTIVE` 멤버다. `SYSTEM_ADMIN`에도 같은 접근 규칙을 적용한다. Space 소유자와 활성 Space `ADMIN`은 Mount 덮어쓰기를 우회한다. [Auth API](auth-api.md) · [Space와 Mount 권한](../security/permissions.md#mount-유효-권한-계산)
- `M` 경로는 먼저 Space 상태와 Mount 소속을 확인하고, 최종 `MOUNT_VIEW`로 Mount를 볼 수 있을 때 요구 관리 권한을 확인한다. `PRIVATE`은 기본 `MOUNT_VIEW`를 제거한다. Role 덮어쓰기와 사용자 덮어쓰기를 적용한 뒤에도 `MOUNT_VIEW`가 없으면 목록에서 제외하고 직접 조회는 404로 처리한다.
- 조회 경로의 Space 상태 허용 목록에는 `ACTIVE`·`ARCHIVED`가 포함된다. 보관된 Space에서도 유효 `MOUNT_MANAGE`는 유지되므로 MT-07·MT-10을 조회할 수 있다. 모든 변경은 `ACTIVE` Space에서 수행하며 `DELETING`·`DELETED` Space는 404로 처리한다. [작업별 인가 계약](../../contracts/permissions/README.md#mount-권한) · [현재 권한 정책](../../src/CloudSharp.Core/Policies/Authorization/MountPermissionPolicy.cs)
- PublicId는 RFC variant UUIDv7이다. JSON 본문을 보내는 요청은 `Content-Type: application/json`을 사용한다. JSON 필드는 `camelCase`, 열거 값과 권한 코드는 `UPPER_SNAKE_CASE`, 시각은 UTC ISO 8601을 사용한다. 성공·오류 응답은 `Cache-Control: no-store`를 적용한다. [공통 API 계약](../../.llm/api/common-contract.md) · [기존 Space API](space-api.md#공통-계약)
- 페이지 조회의 `limit`은 기본 50, 범위 1~100이다. 첫 페이지는 `cursor`를 생략하고 다음 페이지는 같은 조회 조건에서 직전 응답의 `nextCursor`를 그대로 보낸다. 마지막 페이지의 `nextCursor`는 `null`이며 반환할 항목이 없으면 `items`는 `[]`다.

### Mount 버전과 If-Match

MT-04~MT-06과 MT-08~MT-09에 MT-03이나 직전 변경 응답에서 받은 강한 ETag 하나를 보낸다. 예를 들어 `ETag: "v7"`을 받았다면 `If-Match: "v7"`로 요청한다. 설정·상태·권한 덮어쓰기는 같은 Mount 버전을 공유한다. 실제로 변경된 경우에만 버전과 `updatedAt`이 갱신된다. `412 PRECONDITION_FAILED`를 받으면 상세를 다시 조회한 뒤 최신 상태에서 변경을 적용할지 판단한다. [공통 ETag 계약](../../.llm/api/common-contract.md#8-etag와-optimistic-concurrency) · [If-Match 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)

## MT-01 Mount 목록

`GET S/mounts?sort=CREATED_AT&direction=DESC&limit=50`은 요청자가 볼 수 있는 `ACTIVE`·`ARCHIVED` Mount를 반환한다. `sort`는 `CREATED_AT`으로 고정되며 `direction`은 `ASC` 또는 `DESC`이고 기본값은 `DESC`다. 생성 시각이 같으면 `mountPublicId`에 같은 방향을 적용한다. cursor는 Space·요청자·정렬 조건에 묶이며, 다음 페이지에서도 최신 권한으로 항목을 거른다. [목록 Query](../../src/CloudSharp.Core/UseCases/Mounts/Listings/ListMountsQuery.cs) · [목록 저장소](../../src/CloudSharp.Infrastructure/Persistence/Repositories/MountListCandidateReader.cs)

`items`의 각 항목은 상세의 `mountPublicId`, `name`, `visibility`, `status`, `createdAt`, `updatedAt`, `myAccess`를 담는다. `canCreateMount`는 요청자가 `ACTIVE` Space에서 `MOUNT_MANAGE`를 가졌는지 나타내며 빈 페이지에도 포함된다.

## MT-02 Mount 개설

`POST S/mounts`의 본문은 `name`과 `visibility`를 필수로 받는다. 이름은 양끝 공백을 제거한 뒤 1~200자이며 같은 Space에서 중복할 수 있다. `visibility`는 `PUBLIC` 또는 `PRIVATE`다.

```json
{
  "name": "디자인",
  "visibility": "PRIVATE",
  "roleOverwrites": [
    {
      "rolePublicId": "01977b5c-da09-7f41-b4c6-af3af0fdba90",
      "allow": ["MOUNT_VIEW", "FILES_READ"],
      "deny": []
    }
  ]
}
```

- `roleOverwrites`와 `userOverwrites`는 선택 목록이며 생략하면 빈 목록으로 처리한다. 사용자 항목은 `userPublicId`를 사용한다. 유형별 대상은 한 번만 지정할 수 있다. 각 항목에는 Mount 권한 코드를 담은 `allow`와 `deny` 배열을 모두 보내며 하나 이상에 값이 있어야 한다. [허용 권한 코드](#mt-08과-mt-09-권한-덮어쓰기-교체)
- Role은 같은 Space의 삭제되지 않은 Role이어야 한다. 사용자는 활성 계정의 Space 소유자 또는 `ACTIVE` 멤버여야 한다. 요청자는 Space에서 자신이 가진 Mount 범위 권한만 초기 덮어쓰기에 지정할 수 있다.
- 성공하면 Mount와 초기 덮어쓰기를 함께 저장하고 `201 Created`, `Location: M`, `ETag: "v1"`과 [Mount 상세](#mt-03-mount-상세)를 반환한다. Mount는 `ACTIVE`로 시작한다.
- 새 개설 작업에는 출력 가능한 ASCII 16~128자의 `Idempotency-Key` 하나를 보낸다. 응답을 잃으면 같은 사용자·경로·미디어 타입·입력·키로 재시도한다. 최신 활성 Space와 관리 자격이 확인되면 완료 후 24시간 동안 최초 201 본문·`Location`·`ETag`를 재생한다. [멱등 키 사용법](../../src/CloudSharp.Api/README.md#idempotency-key)

## MT-03 Mount 상세

`GET M`은 `ACTIVE`·`ARCHIVED` Mount의 상세 JSON과 해당 스냅샷의 `ETag`를 반환한다. MT-02와 MT-04~MT-06도 같은 본문을 사용한다.

```json
{
  "spacePublicId": "01977b58-32aa-7c60-a17e-0840c6df1200",
  "mountPublicId": "01977b59-01dc-7ea8-9e38-f5abfdb57110",
  "name": "디자인",
  "visibility": "PRIVATE",
  "status": "ACTIVE",
  "myAccess": {
    "permissions": ["MOUNT_VIEW", "MOUNT_MANAGE", "FILES_READ", "FILES_DOWNLOAD", "FILES_WRITE"],
    "canManage": true
  },
  "createdAt": "2026-06-22T12:00:00Z",
  "updatedAt": "2026-06-22T12:05:00Z",
  "archivedAt": null
}
```

`myAccess.permissions`는 요청자가 현재 행사할 수 있는 Mount 범위 권한으로, Space나 Mount가 보관 상태이면 `FILES_WRITE`를 표시하지 않는다. `myAccess.canManage`는 유효 `MOUNT_MANAGE` 보유 여부다. `archivedAt`은 `ARCHIVED` Mount에서만 시각 값을 가진다. 파일 쓰기는 해당 Space와 Mount가 모두 활성 상태이고 유효 `FILES_WRITE`가 있을 때 허용한다. [콘텐츠·전송 계약](../../.llm/api/content-transfer.md)

## MT-04 Mount 설정 변경

`PATCH M`에 `If-Match`와 `name`, `visibility` 중 하나 이상을 담은 JSON을 보낸다. 아래 요청은 공개 범위만 바꾼다.

```http
PATCH /api/v2/spaces/01977b58-32aa-7c60-a17e-0840c6df1200/mounts/01977b59-01dc-7ea8-9e38-f5abfdb57110 HTTP/1.1
Host: api.example.test
Authorization: Bearer <accessToken>
Content-Type: application/json
Content-Length: 23
If-Match: "v7"

{"visibility":"PUBLIC"}
```

명시적 `null`은 필드 오류이며 생략한 필드는 기존 값을 유지한다. 이름 규칙은 개설과 같다. 공개 범위를 바꿔도 저장된 Role·사용자 덮어쓰기는 유지된다. `ARCHIVED` Mount도 설정을 변경할 수 있다. 동일 설정은 현재 상세와 ETag를 반환한다.

## MT-05와 MT-06 Mount 보관과 재활성화

두 요청 모두 `If-Match`를 보내며 요청 본문은 생략한다.

| 경로 | 상태 전이 | 성공 시 `archivedAt` |
|---|---|---|
| `POST M/archive` | `ACTIVE` → `ARCHIVED` | 보관 시각 |
| `POST M/reactivate` | `ARCHIVED` → `ACTIVE` | `null` |

전이의 출발 상태가 다르면 `409 REQUEST_CONFLICT`다. 성공 시 변경된 상세와 ETag를 반환한다.

## MT-07 권한 덮어쓰기 목록

`GET M/permissions?targetType=ROLE&limit=50`은 저장된 덮어쓰기를 대상 PublicId 오름차순으로 반환한다. `targetType`은 필수이며 `ROLE` 또는 `USER`를 보낸다. `limit`과 `cursor`는 공통 규칙을 따른다. `items`는 아래 교체 응답과 같은 항목 배열이다. `ACTIVE`·`ARCHIVED` Mount 모두 조회할 수 있다.

## MT-08과 MT-09 권한 덮어쓰기 교체

Role은 `PUT M/permissions/roles/{rolePublicId}`, 사용자는 `PUT M/permissions/users/{userPublicId}`로 한 대상의 허용·거부를 전체 교체한다. `If-Match`와 두 배열을 보낸다. 허용 코드는 `MOUNT_VIEW`, `MOUNT_MANAGE`, `FILES_READ`, `FILES_DOWNLOAD`, `FILES_WRITE`이며 같은 권한을 두 배열에 넣으면 400이다. `ARCHIVED` Mount도 덮어쓰기를 변경할 수 있다.

```json
{
  "allow": ["MOUNT_VIEW", "FILES_READ"],
  "deny": ["FILES_WRITE"]
}
```

- Role PublicId는 Space Role 목록에서 확인한다. 사용자 경로에는 Space 멤버십의 `userPublicId` 또는 Space 상세의 `ownerUserPublicId`를 사용한다. [Space Role 목록](space-role-api.md) · [Space 멤버십](space-membership-api.md) · [Space 상세](space-api.md)
- 교체에서 설정하는 대상의 상태 조건은 MT-02와 같다. `{"allow":[],"deny":[]}`를 보내면 덮어쓰기를 제거한다. 제거는 같은 Space에서 확인되는 삭제된 Role이나 정지된 멤버에도 적용할 수 있다. 대상이 없거나 다른 Space에 속하면 404다.
- 요청자는 허용·거부 상태가 바뀌는 권한을 해당 Mount에서 모두 가져야 한다. 기존에 설정된 값 중 그대로 유지하는 권한은 요청자가 가지지 않아도 된다. 같은 집합을 재설정하거나 이미 없는 덮어쓰기를 제거하면 현재 ETag를 반환한다.

성공은 200과 Mount `ETag`를 반환한다. 제거 후에는 아래 응답의 `allow`와 `deny`가 모두 `[]`다. 사용자 응답의 `targetType`은 `USER`다.

```json
{
  "targetType": "ROLE",
  "targetPublicId": "01977b5c-da09-7f41-b4c6-af3af0fdba90",
  "allow": ["MOUNT_VIEW", "FILES_READ"],
  "deny": ["FILES_WRITE"]
}
```

## MT-10 유효 권한 미리보기

`GET M/permissions/preview`는 `ACTIVE`·`ARCHIVED` Mount에서 공개 범위 변경의 영향을 확인하는 조회다. `userPublicId`와 `visibility` 중 하나 이상을 보낸다.

| query | 응답 |
|---|---|
| `userPublicId={userPublicId}` | 한 사용자의 현재와 동일한 미리보기. `nextCursor`는 `null`. |
| `userPublicId={userPublicId}&visibility=PRIVATE` | 한 사용자의 현재 권한과 제안한 공개 범위의 권한. `nextCursor`는 `null`. |
| `visibility=PRIVATE&limit=50` | 권한이 달라지는 사용자만 사용자 PublicId 오름차순으로 페이지에 반환. |

`visibility`는 `PUBLIC` 또는 `PRIVATE`다. 직접 지정할 사용자는 활성 계정의 Space 소유자 또는 `ACTIVE` 멤버여야 하며 자격이 없으면 404다. 응답은 `items`와 `nextCursor`를 담는다. 아래 예시는 공개 범위를 `PUBLIC`에서 `PRIVATE`으로 변경하면 기본 `VIEWER`가 열람 권한을 잃는 경우다.

```json
{
  "items": [
    {
      "userPublicId": "01977b5d-4a3d-7a82-bde0-f930d562fe00",
      "currentPermissions": ["MOUNT_VIEW", "FILES_READ", "FILES_DOWNLOAD"],
      "previewPermissions": [],
      "currentSources": [
        {"sourceType": "BASE", "allow": ["MOUNT_VIEW", "FILES_READ", "FILES_DOWNLOAD"], "deny": [], "rolePublicId": null}
      ],
      "previewSources": [
        {"sourceType": "BASE", "allow": ["MOUNT_VIEW", "FILES_READ", "FILES_DOWNLOAD"], "deny": [], "rolePublicId": null},
        {"sourceType": "VISIBILITY", "allow": [], "deny": ["MOUNT_VIEW"], "rolePublicId": null}
      ]
    }
  ],
  "nextCursor": null
}
```

근거 배열은 적용 순서대로 `ADMINISTRATOR`, `BASE`, `VISIBILITY`, `ROLE`, `USER` 중 실제로 적용된 항목을 담는다. `rolePublicId`는 `ROLE`에만 값이 있다. 실제 변경 요청은 최신 권한과 `If-Match`를 다시 확인한다. [미리보기 유즈케이스](../../src/CloudSharp.Core/UseCases/Mounts/PermissionPreviews/PreviewMountPermissionsUseCase.cs)

## 오류 응답

오류 본문은 `requestId`와 `error` 객체를 반환한다. `error`의 필드는 `code`, `message`, `details`이며 `requestId`는 응답의 `X-Correlation-ID`와 같다. 클라이언트 분기에는 `error.code`를 사용한다. [공통 오류 계약](../../.llm/api/common-contract.md#6-오류-응답)

| HTTP | 오류 코드 | 조건 |
|---|---|---|
| 400 | `REQUEST_VALIDATION_FAILED` | UUID·JSON·query·권한 코드 오류, 잘못된 `If-Match`(`ifMatch` / `INVALID_ETAG`), 누락되거나 잘못된 개설 멱등 키(`idempotencyKey` / `INVALID_IDEMPOTENCY_KEY`) |
| 400 | `CURSOR_INVALID` | 잘못됐거나 현재 조회 범위에 맞지 않는 cursor |
| 401 | `AUTH_TOKEN_REQUIRED`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED` | 인증 누락·실패 또는 허용되지 않는 credential 종류 |
| 403 | `PERMISSION_DENIED` | Mount를 볼 수 있는 요청자의 관리 권한 부족, 개설 권한 부족 또는 갖고 있지 않은 권한을 덮어쓰기로 변경 |
| 404 | `RESOURCE_NOT_FOUND` | Space 접근 자격 없음, 삭제 중·삭제된 Space, 미존재·다른 Space의 리소스, Mount의 유효 `MOUNT_VIEW` 부재 또는 미리보기 대상 자격 없음 |
| 409 | `SPACE_ARCHIVED` | 보관된 Space에 대한 개설·설정·상태·덮어쓰기 변경 |
| 409 | `REQUEST_CONFLICT` | Mount 전이의 출발 상태가 다르거나, 덮어쓰기를 설정할 대상이 같은 Space에 있으나 허용 상태가 아님 |
| 409 | `IDEMPOTENCY_KEY_REUSED`, `IDEMPOTENCY_REQUEST_IN_PROGRESS` | 개설에서 같은 키에 다른 입력을 사용했거나 같은 키의 요청이 처리 중 |
| 412 | `PRECONDITION_FAILED` | Mount ETag 불일치 또는 저장 시 버전 충돌 |
| 428 | `PRECONDITION_REQUIRED` | 기존 Mount 변경에 `If-Match` 누락 |
| 503 | `DEPENDENCY_UNAVAILABLE` | 요청 처리에 필요한 저장소에 접근할 수 없음 |

## 엔드포인트 연결

- MT-01과 MT-02는 `SpaceAuthorizationFilter`가 제공하는 `AuthorizedSpaceContext`를 Query·Command에 넘긴다. MT-03~MT-10은 `MountAuthorizationFilter`의 `AuthorizedMountContext`를 사용한다. 관리 조회·변경에서는 유효 `MOUNT_VIEW`를 확인한 요청의 관리 권한 부족을 403으로 매핑한다. [Mount 인가 필터](../../src/CloudSharp.Api/Endpoints/Mounts/MountAuthorizationFilter.cs) · [인가 정책](../../src/CloudSharp.Core/Policies/Authorization/MountAuthorizationPolicy.cs)
- `MountResultDto.Mount`는 상세 JSON, `MountPermissionResultDto.Overwrite`는 덮어쓰기 JSON으로 매핑한다. 두 결과의 `Version`은 `ETag`에 사용한다. MT-02는 인가 필터 뒤에 멱등 처리 필터를 연결하고 `MountResultDto`를 트랜잭션 완료 결과로 사용해 최초 201을 복구한다. [멱등 처리 연결](../../src/CloudSharp.Api/README.md#opt-in-a-route) · [Mount 저장 계약](../repositories/mount-repositories.md)

## 후속 사용량 조회

MT-11 `GET M/usage`는 Content 구현 이후에 추가한다. 상세와 같은 `MOUNT_VIEW` 인가 후 `PURGED` 이외 파일의 확정 크기 합계를 64비트 `usedBytes`로 반환한다. 파일이 없으면 `0`이며 사용량은 Mount 버전과 독립적으로 갱신된다. [사용량 설계](../../.llm/api/mounts.md#사용량-조회)
