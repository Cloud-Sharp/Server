# Mount 유즈케이스

CloudSharp Production v1의 Mount 조회·개설·설정·보관과 Role·사용자별 파일 권한을 다룬다. 아래 10개 작업은 구현 대상이다. 현재 Core에는 `Mount`와 `MountAccessGrant`, 도메인 단위 테스트가 있다. [Mount API 설계](../../.llm/api/mounts.md) · [Mount 도메인 설계](../../.llm/domains/mount.md) · [Mount 도메인 코드](../../src/CloudSharp.Core/Domain/Mounts/Mount.cs)

## 공통 인가 규칙

- `S`는 `/api/v2/spaces/{spacePublicId}`, `M`은 `S/mounts/{mountPublicId}`다. 행위자는 활성 계정의 Session 사용자다. 리소스 식별자는 UUIDv7 PublicId를 사용한다.
- 접근 자격은 최신 Space 소유권 또는 `ACTIVE` Membership으로 확인한다. Mount 관리자는 Space 소유자 또는 `ACTIVE` Membership에 시스템 `ADMIN` Role이 연결된 사용자다. `ACTIVE`·`ARCHIVED` Space에서 관리 조회를 허용하고, 관리 변경에는 `ACTIVE` Space가 필요하다. `DELETING`·`DELETED` Space는 404로 처리한다.
- `PUBLIC`은 접근 자격이 있는 모든 사용자에게, `PRIVATE`은 Space 소유자와 활성 `ADMIN`에게 기본 `MOUNT_FILES_READ`·`MOUNT_FILES_DOWNLOAD`를 부여한다. 기본 권한과 현재 Role에 부여된 권한, 사용자 직접 부여의 합집합을 계산한다. 편집 권한은 명시적으로 부여한다.
- 목록·검색·상세와 직접 링크를 열려면 `MOUNT_FILES_READ`가 필요하다. 다운로드와 파일 변경은 각각 `MOUNT_FILES_DOWNLOAD`와 `MOUNT_FILES_WRITE`를 추가로 확인한다. Space 또는 Mount가 보관되면 유효 권한에서 쓰기를 제외하고, 보관된 Mount의 설정과 부여는 활성 Space의 관리자가 계속 변경할 수 있다.
- 목록과 단건의 `myAccess.canManage`는 현재 설정을 변경할 수 있는지 표시한다. 보관된 Space에서는 `false`여도 관리자에게 권한 목록과 미리보기를 제공한다. [Mount 인가 규칙](../../.llm/domains/mount.md#인가-규칙) · [Space 유즈케이스](space-use-cases.md)

## 유즈케이스 목록

| ID | 유즈케이스 | API | 행위자·조건 | 성공 결과 |
|---|---|---|---|---|
| MT-01 | 접근 가능한 Mount 목록 조회 | `GET S/mounts` | Space 접근 자격 | 200, `items`, `nextCursor`, `canCreateMount` |
| MT-02 | Mount 개설 | `POST S/mounts` | 관리자, 활성 Space, `Idempotency-Key` | 201, Mount 상세, `Location: M`, Mount ETag |
| MT-03 | Mount 상세 조회 | `GET M` | `MOUNT_FILES_READ` | 200, Mount 상세와 ETag |
| MT-04 | Mount 설정 변경 | `PATCH M` | 관리자, 활성 Space, Mount `If-Match` | 200, 변경된 상세와 ETag |
| MT-05 | Mount 보관 | `POST M/archive` | 관리자, 활성 Space, Mount `If-Match`, `ACTIVE` Mount | 200, 보관 상세와 ETag |
| MT-06 | Mount 재활성화 | `POST M/reactivate` | 관리자, 활성 Space, Mount `If-Match`, `ARCHIVED` Mount | 200, 재활성화 상세와 ETag |
| MT-07 | 명시적 권한 부여 목록 조회 | `GET M/permissions` | 관리자, `targetType=ROLE` 또는 `USER` | 200, 부여 항목과 `nextCursor` |
| MT-08 | Role 권한 전체 교체 | `PUT M/permissions/roles/{rolePublicId}` | 관리자, 활성 Space, Mount `If-Match` | 200, 대상의 부여 결과와 Mount ETag |
| MT-09 | 사용자 직접 권한 전체 교체 | `PUT M/permissions/users/{userPublicId}` | 관리자, 활성 Space, Mount `If-Match` | 200, 대상의 부여 결과와 Mount ETag |
| MT-10 | 유효 권한 미리보기 | `GET M/permissions/preview` | 관리자, `userPublicId` 또는 `visibility` | 200, 현재·제안 권한과 근거, `nextCursor` |

## 조회와 권한 미리보기

- **MT-01**은 `ACTIVE`·`ARCHIVED` Mount 중 요청자가 읽을 수 있는 항목으로 페이지를 채운다. `CREATED_AT`의 기본 방향은 `DESC`이며 같은 생성 시각은 `mountPublicId`로 정렬한다. `limit`은 기본 50, 범위 1~100이다. 서명한 cursor는 Space·행위자·정렬 조건에 묶고, 다음 페이지에서도 최신 권한을 평가한다. `canCreateMount`는 활성 Space의 관리자에게 `true`이며, 빈 첫 페이지에서도 제공한다.
- **MT-03**은 이름·공개 범위·상태·시각, Space와 Mount PublicId, 루트의 `rootFolderPublicId`, 표시용 `usedBytes`, `myAccess`를 반환한다. ETag는 함께 조회한 Mount 버전에서 만든다. MT-02와 MT-04~06도 같은 상세 형식을 사용한다.
- **MT-07**은 지정한 한 유형의 명시적 부여를 대상 PublicId 오름차순으로 반환한다. 각 항목은 `targetType`, `targetPublicId`, `permissions`다. 대상이 얻는 기본·다른 Role·다른 직접 부여의 유효 권한 계산은 MT-10에서 제공한다.
- **MT-10**은 `userPublicId`를 주면 한 사용자의 결과와 `nextCursor: null`을 반환한다. `visibility`를 함께 주면 제안한 공개 범위를 적용해 비교하고, 생략하면 현재와 미리보기 결과가 같다. `visibility`만 주면 유효 권한이 달라지는 사용자를 대상 PublicId 오름차순으로 페이지에 담는다. 결과에는 `userPublicId`, `currentPermissions`, `previewPermissions`, `currentSources`, `previewSources`가 포함된다. 근거의 `sourceType`은 `DEFAULT`, `ROLE`, `USER`이며 `rolePublicId`는 `ROLE`에만 값이 있다. 직접 지정한 사용자는 활성 계정이면서 Space 소유자 또는 `ACTIVE` Membership이어야 하며, 정지된 계정·`SUSPENDED`/`LEFT` Membership·다른 Space 사용자는 빈 권한 대신 `RESOURCE_NOT_FOUND`로 마스킹한다. 각 근거의 권한은 최종 유효 권한과 교집합으로 표시하고 빈 근거는 생략하므로, 보관으로 제거된 `MOUNT_FILES_WRITE`만 가진 근거는 결과에 남기지 않는다. 미리보기는 조회만 수행하며 이후 저장 요청은 최신 인가와 버전을 다시 검사한다. [권한 조회 계약](../../.llm/api/mounts.md#권한-설정)

## 변경 흐름과 트랜잭션

1. **MT-02 개설.** 활성 Space의 관리 자격을 확인한다. `name`은 trim 후 1~200자이며 같은 Space 안에서 중복할 수 있다. `visibility`는 `PUBLIC` 또는 `PRIVATE`다. 선택 필드 `roleGrants`·`userGrants`의 생략은 빈 목록으로 처리한다. 각 초기 부여에 정의된 권한이 하나 이상 필요하며 유형별 중복 대상은 거부한다. 대상 PublicId를 같은 Space의 유효한 내부 ID로 해석해 `MountAccessGrant.Create`에 전달한다.
2. **개설 저장과 재생.** `Mount.Create`로 `ACTIVE`, 버전 1의 Mount를 만든다. Mount, 초기 부여, 루트 `DirectoryEntry`와 `Folder`, 멱등 완료 레코드를 한 PostgreSQL 트랜잭션으로 커밋한다. `Idempotency-Key`는 호출자·메서드·Space 경로에 묶는다. 완료된 동일 요청은 필터에서 재생하므로 필터 진입 전에 최신 활성 Space와 관리 자격을 확인하고, 최초 쓰기는 트랜잭션 안에서도 확인한다. 같은 입력의 완료 요청은 기존 201 본문·`Location`·ETag를 재생한다. 같은 키에 다른 입력은 `409 IDEMPOTENCY_KEY_REUSED`, 처리 중인 동일 요청은 `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`다. [Mount 테이블 설계](../../.llm/database/mounts.md) · [현재 Idempotency-Key 구현](../../src/CloudSharp.Api/README.md#idempotency-key)
3. **기존 Mount 변경.** MT-04~06과 MT-08~09는 최신 관리 자격과 소속을 확인하고, 트랜잭션에서 읽은 Mount의 버전을 `If-Match`와 대조한다. `IClock.UtcNow`를 도메인 행위에 전달한다. 실제 변경만 Mount 버전을 올리며, 저장 시 읽은 버전으로 낙관적 동시성을 확인한다. 권한 행의 추가·갱신·삭제와 Mount 버전·수정 시각을 한 트랜잭션에 저장한다. [기존 Space 변경 흐름](../../src/CloudSharp.Core/UseCases/Spaces/SpaceMutationWorkflow.cs) · [권한 변경 저장 계약](../../.llm/database/mounts.md#권한-변경과-용량)
4. **MT-04~06 설정과 상태.** `PATCH`는 `name`, `visibility` 중 하나 이상을 받고 명시적 `null`은 거부한다. `UpdateSettings`로 전달한 필드만 변경하며, 공개 범위를 바꿔도 명시적 부여는 유지한다. 같은 설정은 현재 ETag로 성공한다. `Archive`·`Reactivate`에 잘못된 출발 상태를 전달하면 `409 REQUEST_CONFLICT`다. 재활성화 후의 콘텐츠 쓰기에는 현재 유효 권한이 필요하다.
5. **MT-08~09 권한 교체.** 같은 Space의 삭제되지 않은 Role, 또는 활성 계정이면서 Space 소유자나 활성 멤버인 사용자를 해석한다. `SetGrant`에 해당 대상의 전체 권한 집합을 넘긴다. 빈 배열은 회수이며 정지된 멤버나 삭제된 Role의 기존 부여도 제거할 수 있다. 동일 집합 재설정과 이미 비어 있는 부여의 회수는 기존 ETag를 반환한다. 응답은 해당 대상의 명시적 부여 결과이며, 기본·Role·나머지 직접 부여는 다음 인가 판단에서 다시 합산한다. [권한 설정 계약](../../.llm/api/mounts.md#권한-설정)

## 오류 계약

| 상황 | API 결과 |
|---|---|
| 잘못된 UUID, 필드, 권한 코드, `If-Match` 형식 | 400 `REQUEST_VALIDATION_FAILED` |
| 잘못됐거나 현재 조회 조건과 맞지 않는 cursor | 400 `CURSOR_INVALID` |
| Session 인증 누락·실패 또는 지원하지 않는 actor | 401 공통 인증 오류 |
| Mount를 볼 수 있지만 관리 자격 없음 | 403 `PERMISSION_DENIED` |
| 미존재, 다른 Space의 리소스, Mount 열람 불가 | 404 `RESOURCE_NOT_FOUND` |
| 보관된 Space에 대한 관리 변경 | 409 `SPACE_ARCHIVED` |
| 현재 상태에서 허용되지 않는 전이 또는 부여 | 409 `REQUEST_CONFLICT` |
| Mount ETag 불일치 또는 저장 시 버전 충돌 | 412 `PRECONDITION_FAILED` |
| 기존 Mount 변경의 `If-Match` 누락 | 428 `PRECONDITION_REQUIRED` |

Mount 소속과 열람 자격을 먼저 확인해 상태나 버전이 노출되기 전에 404를 적용한다. [Mount 오류 계약](../../.llm/api/mounts.md#조건부-요청과-오류)

## Space·콘텐츠 연동과 검증 기준

- **멤버십.** 정지하면 모든 Mount 접근을 중단하고 직접 부여를 보존한다. 재활성화하면 현재 부여를 다시 평가한다. 제거·탈퇴로 `LEFT`가 될 때는 동일 Space의 모든 Mount에서 사용자의 직접 부여를 일괄 회수하고 영향받은 Mount만 갱신한다. 멤버십 변경과 회수는 한 트랜잭션으로 처리한다. 초기 부여를 포함한 직접 부여와 `LEFT` 전환은 동일 Space·사용자 단위로 직렬화하고 잠금을 획득한 뒤 자격을 다시 확인한다. 소유권 이전 시 기본 접근은 새 소유자 기준으로 계산하며 직접 부여는 각 사용자에게 남는다. [Membership 유즈케이스](space-use-cases.md#3-space-membership) · [직접 부여 저장 계약](../../.llm/database/mounts.md#mount_user_grants)
- **콘텐츠와 용량.** Space는 Mount 없이 시작할 수 있다. 개설 실패 시 루트와 초기 부여도 롤백하며, 확정된 Mount에는 루트가 하나 있어야 한다. 콘텐츠 조회와 변경은 Space·Mount·대상의 소속을 확인한다. `usedBytes`는 해당 Mount의 `PURGED` 이외 파일 크기 합계이며, 업로드 허용·예약은 Space 전체 quota로 판단한다. Mount 보관은 해당 Mount의 파일 쓰기를 막고 Space 보관은 모든 소속 Mount의 파일 쓰기를 막는다. [루트·용량 도메인 계약](../../.llm/domains/mount.md#루트-폴더와-용량)
- **검증.** 일반 멤버의 공용·제한 Mount 조회, 소유자·ADMIN의 명시적 편집 권한, 제한 Mount의 404 마스킹을 확인한다. 비가시 항목이 정렬 중간에 있어도 목록 페이지를 채우고, cursor 재사용 시 변경된 멤버십과 Role을 반영하는지 검증한다. 개설의 롤백·재생, 동시 권한 변경의 ETag 충돌, 직접 부여와 탈퇴의 경합, 두 종류의 보관 상태도 검증한다. [기존 Mount 도메인 테스트](../../tests/CloudSharp.Core.Tests/Domain/Mounts/MountTests.cs)

## 구현 전 확정할 계약

- MT-08~09에서 빈 배열을 보냈지만 대상이 더 이상 부여 자격을 갖지 않고 기존 부여도 없는 경우의 응답을 확정한다. 현재 설계는 기존 부여의 정리와 이미 비어 있는 유효 대상의 멱등 성공을 명시한다. [권한 설정과 미리보기](../../.llm/api/mounts.md#권한-설정)
