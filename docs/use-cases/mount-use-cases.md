# Mount 유즈케이스

CloudSharp Production v1의 Mount 조회·개설·설정·보관·사용량과 Role·사용자별 Mount 권한 덮어쓰기를 다룬다. [Space와 Mount 권한](../security/permissions.md) · [Mount 권한 계약](../../contracts/permissions/README.md#mount-권한) · [Mount 도메인 코드](../../src/CloudSharp.Core/Domain/Mounts/Mount.cs)

## 공통 인가 규칙

- `S`는 `/api/v2/spaces/{spacePublicId}`, `M`은 `S/mounts/{mountPublicId}`다. 행위자는 활성 계정의 Session 사용자다. 리소스 식별자는 UUIDv7 PublicId를 사용한다.
- 권한은 Discord 채널 권한처럼 계산한다. Space Role의 Mount 범위 권한(`MOUNT_VIEW`, `MOUNT_MANAGE`, `FILES_READ`, `FILES_DOWNLOAD`, `FILES_WRITE`)이 기본값이고, `PRIVATE`은 기본 `MOUNT_VIEW`를 거부한다. Role 덮어쓰기(거부 후 허용), 사용자 덮어쓰기(거부 후 허용) 순으로 적용한다. Space 소유자와 활성 `ADMIN`은 덮어쓰기를 우회한다.
- 암묵적 권한: 최종 `MOUNT_VIEW`가 없으면 모든 유효 Mount 권한을 비운다. 예를 들어 `PRIVATE` Mount에서 일반 멤버에게 `MOUNT_MANAGE`만 허용하면 해당 멤버는 Mount를 조회하거나 관리할 수 없다. `FILES_READ`가 없으면 `FILES_DOWNLOAD`·`FILES_WRITE`도 뺀다. 보관된 Space에서는 `MOUNT_MANAGE`·`FILES_WRITE`를 뺀다.
- 요구 권한은 endpoint에서 선언한다. Mount 경로(`M`)는 `MountAuthorizationFilter`, 목록과 개설은 `SpaceAuthorizationFilter`가 평가하고, 유즈케이스는 인가된 context(`AuthorizedMountContext` 또는 `AuthorizedSpaceContext`)를 Command·Query로 받는다. 유즈케이스는 권한을 다시 검사하지 않고 트랜잭션 안에서 최신 Space 상태, Mount 소속, 버전만 확인한다. `DELETING`·`DELETED` Space는 404로 처리한다.
- 목록과 단건의 `myAccess.permissions`는 Mount 범위 유효 권한이고 `myAccess.canManage`는 유효 `MOUNT_MANAGE`의 보유 여부다. [Mount 권한 정책](../../src/CloudSharp.Core/Policies/Authorization/MountPermissionPolicy.cs) · [Mount 인가 필터](../../src/CloudSharp.Api/Endpoints/Mounts/MountAuthorizationFilter.cs) · [Space 유즈케이스](space-use-cases.md)

## 유즈케이스 목록

| ID | 유즈케이스 | API | 행위자·조건 | 성공 결과 |
|---|---|---|---|---|
| MT-01 | 접근 가능한 Mount 목록 조회 | `GET S/mounts` | Space 접근 자격, 항목별 `MOUNT_VIEW` | 200, `items`, `nextCursor`, `canCreateMount` |
| MT-02 | Mount 개설 | `POST S/mounts` | Space의 `MOUNT_MANAGE`, 활성 Space, `Idempotency-Key` | 201, Mount 상세, `Location: M`, Mount ETag |
| MT-03 | Mount 상세 조회 | `GET M` | `MOUNT_VIEW` | 200, Mount 상세와 ETag |
| MT-04 | Mount 설정 변경 | `PATCH M` | `MOUNT_MANAGE`, 활성 Space, Mount `If-Match` | 200, 변경된 상세와 ETag |
| MT-05 | Mount 보관 | `POST M/archive` | `MOUNT_MANAGE`, 활성 Space, Mount `If-Match`, `ACTIVE` Mount | 200, 보관 상세와 ETag |
| MT-06 | Mount 재활성화 | `POST M/reactivate` | `MOUNT_MANAGE`, 활성 Space, Mount `If-Match`, `ARCHIVED` Mount | 200, 재활성화 상세와 ETag |
| MT-07 | 권한 덮어쓰기 목록 조회 | `GET M/permissions` | `MOUNT_MANAGE`, `targetType=ROLE` 또는 `USER` | 200, 덮어쓰기 항목과 `nextCursor` |
| MT-08 | Role 덮어쓰기 전체 교체 | `PUT M/permissions/roles/{rolePublicId}` | `MOUNT_MANAGE`, 바뀌는 권한 보유, 활성 Space, Mount `If-Match` | 200, 대상의 덮어쓰기 결과와 Mount ETag |
| MT-09 | 사용자 덮어쓰기 전체 교체 | `PUT M/permissions/users/{userPublicId}` | `MOUNT_MANAGE`, 바뀌는 권한 보유, 활성 Space, Mount `If-Match` | 200, 대상의 덮어쓰기 결과와 Mount ETag |
| MT-10 | 유효 권한 미리보기 | `GET M/permissions/preview` | `MOUNT_MANAGE`, `userPublicId` 또는 `visibility` | 200, 현재·제안 권한과 근거, `nextCursor` |
| MT-11 | Mount 사용량 조회 (Content 구현 이후) | `GET M/usage` | `MOUNT_VIEW` | 200, `usedBytes` |

표의 경로는 HTTP 설계다. 이 계약은 현재 Core 유즈케이스에서 사용한다.

## 조회와 권한 미리보기

- **MT-01**은 `ACTIVE`·`ARCHIVED` Mount 중 요청자에게 유효 `MOUNT_VIEW`가 있는 항목으로 페이지를 채운다. `CREATED_AT`의 기본 방향은 `DESC`이며 같은 생성 시각은 `mountPublicId`로 정렬한다. `limit`은 기본 50, 범위 1~100이다. 서명한 cursor는 Space·행위자·정렬 조건에 묶고, 다음 페이지에서도 최신 권한을 평가한다. `canCreateMount`는 활성 Space의 유효 `MOUNT_MANAGE`를 나타내며, 빈 첫 페이지에서도 제공한다.
- **MT-03**은 이름·공개 범위·상태·시각, Space와 Mount PublicId, `myAccess`를 반환한다. ETag는 함께 조회한 Mount 버전에서 만든다. MT-02와 MT-04~06도 같은 상세 형식을 사용한다.
- **MT-11**은 Content 구현을 마친 뒤 추가한다. MT-03과 같은 열람 자격을 확인한 뒤 사용량 Reader를 호출한다. 같은 Mount의 `PURGED` 이외 파일 크기 합계를 64비트 `usedBytes`로 반환하며 파일이 없으면 0이다. 집계에는 Mount 버전과 독립적으로 바뀌는 콘텐츠가 포함되므로 Mount ETag를 사용하지 않는다.
- **MT-07**은 지정한 한 유형의 덮어쓰기를 대상 PublicId 오름차순으로 반환한다. 각 항목은 `targetType`, `targetPublicId`, `allow`, `deny`다. 대상의 유효 권한 계산은 MT-10에서 제공한다.
- **MT-10**은 `userPublicId`를 주면 한 사용자의 결과와 `nextCursor: null`을 반환한다. `visibility`를 함께 주면 제안한 공개 범위를 적용해 비교하고, 생략하면 현재와 미리보기 결과가 같다. `visibility`만 주면 유효 권한이 달라지는 사용자를 대상 PublicId 오름차순으로 페이지에 담는다. 결과에는 `userPublicId`, `currentPermissions`, `previewPermissions`, `currentSources`, `previewSources`가 포함된다. 근거는 적용 순서대로 `sourceType`(`ADMINISTRATOR`, `BASE`, `VISIBILITY`, `ROLE`, `USER`), `allow`, `deny`, `rolePublicId`를 담으며 `rolePublicId`는 `ROLE`에만 값이 있다. 직접 지정한 사용자는 활성 계정이면서 Space 소유자 또는 `ACTIVE` Membership이어야 한다. 정지된 계정·`SUSPENDED`/`LEFT` Membership·다른 Space 사용자는 `RESOURCE_NOT_FOUND`로 마스킹한다. 근거는 적용된 원래 허용·거부 값이며 빈 근거는 생략한다. 제안으로 `MOUNT_VIEW`를 잃는 사용자는 `previewPermissions`가 빈 집합이고, `previewSources`에서 `VISIBILITY` 등 그 이유를 확인할 수 있다. 이후 저장 요청은 최신 인가와 버전을 다시 검사한다. [권한 미리보기 유즈케이스](../../src/CloudSharp.Core/UseCases/Mounts/PermissionPreviews/PreviewMountPermissionsUseCase.cs)

## 변경 흐름과 트랜잭션

1. **MT-02 개설.** Space filter가 활성 Space의 `MOUNT_MANAGE`를 확인하고, 유즈케이스는 트랜잭션 안에서 Space가 여전히 활성인지 확인한다. `name`은 trim 후 1~200자이며 같은 Space 안에서 중복할 수 있다. `visibility`는 `PUBLIC` 또는 `PRIVATE`다. 선택 필드 `roleOverwrites`·`userOverwrites`의 생략은 빈 목록으로 처리한다. 각 초기 덮어쓰기의 `allow`·`deny`는 Mount 범위 권한만 담고 겹칠 수 없으며 둘 다 비어 있을 수 없다. 유형별 중복 대상은 거부한다. 대상 PublicId를 같은 Space의 유효한 내부 ID로 해석해 `MountPermissionOverwrite.Create`에 전달한다. 초기 덮어쓰기의 권한은 요청자가 Space에서 가진 Mount 범위 권한이어야 하며 아니면 403이다.
2. **개설 저장과 재생.** `Mount.Create`로 `ACTIVE`, 버전 1의 Mount를 만든다. Mount, 초기 권한 부여, 멱등 완료 레코드를 한 PostgreSQL 트랜잭션으로 커밋한다. 응답에는 Mount의 PublicId를 사용한다. `Idempotency-Key`는 호출자·메서드·Space 경로에 묶는다. 완료된 동일 요청은 필터에서 재생하므로 필터 진입 전에 최신 활성 Space와 관리 자격을 확인하고, 최초 쓰기는 트랜잭션 안에서도 확인한다. 같은 입력의 완료 요청은 기존 201 본문·`Location`·ETag를 재생한다. 같은 키에 다른 입력은 `409 IDEMPOTENCY_KEY_REUSED`, 처리 중인 동일 요청은 `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`다. [Mount 테이블 설계](../../.llm/database/mounts.md) · [현재 Idempotency-Key 구현](../../src/CloudSharp.Api/README.md#idempotency-key)
3. **기존 Mount 변경.** Mount filter가 유효 `MOUNT_MANAGE`를 확인한다. 유즈케이스는 트랜잭션에서 최신 Space가 활성인지와 인가된 Mount의 소속을 다시 읽어 확인하고, Mount의 버전을 `If-Match`와 대조하고 `IClock.UtcNow`를 도메인 행위에 전달한다. 실제 변경만 Mount 버전을 올리며, 저장 시 읽은 버전으로 낙관적 동시성을 확인한다. 덮어쓰기 행의 추가·갱신·삭제와 Mount 버전·수정 시각을 한 트랜잭션에 저장한다. [권한 변경 저장 계약](../repositories/mount-repositories.md#저장-선행-조건과-트랜잭션)
4. **MT-04~06 설정과 상태.** `PATCH`는 `name`, `visibility` 중 하나 이상을 받고 명시적 `null`은 거부한다. `UpdateSettings`로 전달한 필드만 변경하며, 공개 범위를 바꿔도 명시적 부여는 유지한다. 같은 설정은 현재 ETag로 성공한다. `Archive`·`Reactivate`에 잘못된 출발 상태를 전달하면 `409 REQUEST_CONFLICT`다.
5. **MT-08~09 덮어쓰기 교체.** 같은 Space의 삭제되지 않은 Role, 또는 활성 계정이면서 Space 소유자나 활성 멤버인 사용자를 해석한다. 허용·거부 상태가 바뀌는 권한은 요청자의 Mount 유효 권한에 있어야 하며 아니면 403이다. `SetOverwrite`에 해당 대상의 전체 `allow`·`deny`를 넘긴다. 둘 다 빈 배열이면 제거이며, 같은 Space에 존재함을 확인할 수 있는 삭제된 Role이나 정지된 멤버에도 허용한다. 이미 없으면 빈 결과와 현재 Mount 버전을 반환하며 등록·저장 없이 성공한다. 미존재·다른 Space 대상은 빈 배열이어도 404다. 동일 집합 재설정도 기존 ETag를 반환한다. [Mount 권한 계약](../../contracts/permissions/README.md#mount-권한)

## 오류 계약

| 상황 | API 결과 |
|---|---|
| 잘못된 UUID, 필드, 권한 코드, `If-Match` 형식 | 400 `REQUEST_VALIDATION_FAILED` |
| 잘못됐거나 현재 조회 조건과 맞지 않는 cursor | 400 `CURSOR_INVALID` |
| Session 인증 누락·실패 또는 지원하지 않는 actor | 401 공통 인증 오류 |
| Mount를 볼 수 있지만 요구 권한 없음, 가지지 않은 권한의 덮어쓰기 변경 | 403 `PERMISSION_DENIED` |
| 미존재, 다른 Space의 리소스, 유효 `MOUNT_VIEW` 없음 | 404 `RESOURCE_NOT_FOUND` |
| 보관된 Space에 대한 관리 변경 | 409 `SPACE_ARCHIVED` |
| 현재 상태에서 허용되지 않는 전이·덮어쓰기 대상 또는 endpoint가 허용하지 않는 Mount 상태 | 409 `REQUEST_CONFLICT` |
| Mount ETag 불일치 또는 저장 시 버전 충돌 | 412 `PRECONDITION_FAILED` |
| 기존 Mount 변경의 `If-Match` 누락 | 428 `PRECONDITION_REQUIRED` |

Mount filter가 소속과 `MOUNT_VIEW`를 먼저 확인한 뒤 Mount 상태와 요구 권한을 확인한다. 소속이나 `MOUNT_VIEW`를 확인할 수 없으면 404다. [Mount 인가 정책](../../src/CloudSharp.Core/Policies/Authorization/MountAuthorizationPolicy.cs) · [Mount 변경 흐름](../../src/CloudSharp.Core/UseCases/Mounts/MountMutationWorkflow.cs)

## Space·콘텐츠 연동과 검증 기준

- **멤버십.** 정지하면 모든 Mount 접근을 중단하고 사용자 덮어쓰기를 보존한다. 재활성화하면 현재 덮어쓰기를 다시 평가한다. 제거·탈퇴로 `LEFT`가 될 때는 동일 Space의 모든 Mount에서 사용자의 덮어쓰기를 일괄 제거하고 영향받은 Mount만 갱신한다. 멤버십 변경과 제거는 한 트랜잭션으로 처리한다. 초기 덮어쓰기를 포함한 사용자 덮어쓰기와 `LEFT` 전환은 동일 Space·사용자 단위로 직렬화하고 잠금을 획득한 뒤 자격을 다시 확인한다. 소유권 이전 시 관리자 우회는 새 소유자 기준으로 계산하며 사용자 덮어쓰기는 각 사용자에게 남는다. [Membership 유즈케이스](space-use-cases.md#3-space-membership) · [직접 부여 저장 계약](../../.llm/database/mounts.md#mount_user_grants)
- **콘텐츠 탐색과 변경.** Space는 Mount 없이 시작할 수 있고, 각 Mount는 빈 상태에서 자체 PublicId로 파일 탐색의 진입점이 된다. 콘텐츠 endpoint는 Mount filter로 `FILES_READ`·`FILES_DOWNLOAD`·`FILES_WRITE`를 요구하며, 이 권한은 Space Role 기본값에 해당 Mount의 덮어쓰기를 적용한 유효 권한이다. `GET M/children`은 부모 폴더가 null인 최상위 파일·폴더를 조회한다. 부모 폴더를 지정하면 같은 Mount에 속해야 하며, 최상위와 하위의 각 위치에서 파일·폴더가 이름 유일성 규칙을 공유한다. 이동과 복원의 명시적 null은 Mount 바로 아래를 뜻한다. 이동에서 부모를 생략하면 현재 위치를 유지하고, 복원에서 생략하면 원래 위치를 사용한다. [콘텐츠 권한 계약](../../contracts/permissions/README.md#콘텐츠전송-endpoint)
- **용량과 쓰기.** 업로드 허용·예약은 Space 전체 quota로 판단한다. Mount 보관은 해당 Mount의 파일 쓰기를 막고 Space 보관은 모든 소속 Mount의 파일 쓰기를 막는다. [콘텐츠 트리와 용량](../../.llm/domains/mount.md#콘텐츠-트리와-용량)
- **검증.** Role·사용자 허용·거부의 적용 순서, `ADMIN` 우회, 공용·제한 Mount 조회, `MOUNT_VIEW`·`FILES_READ` 부재에 따른 암묵적 무효화와 404 마스킹, 가지지 않은 권한의 덮어쓰기 거부를 확인한다. 비가시 항목이 정렬 중간에 있어도 목록 페이지를 채우고, cursor 재사용 시 변경된 멤버십과 Role을 반영하는지 검증한다. 개설의 롤백·재생, 동시 권한 변경의 ETag 충돌, 직접 부여와 탈퇴의 경합, 두 종류의 보관 상태도 검증한다. [기존 Mount 도메인 테스트](../../tests/CloudSharp.Core.Tests/Domain/Mounts/MountTests.cs)
