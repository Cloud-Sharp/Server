# permissions

Space 권한 매트릭스와 endpoint별 인가 계약. Session 사용자 정책을 먼저 확정하며 MCP scope와 교집합 연결은 후속 작업이다.

## 허용 대상과 기본 접근 자격

- `USER`: 활성 계정으로 인증된 Session 사용자. Space 작업은 아래 Permission 또는 리소스 관계 조건까지 충족해야 한다. OWNER도 USER에 포함된다.
- `OWNER`: 해당 Space의 `owner_user_id`와 현재 사용자 ID가 같은 사용자. 전역 SystemRole이 아니다.
- 일반 Space 접근 자격은 OWNER 또는 해당 Space의 ACTIVE Membership이다. Session SYSTEM_ADMIN의 Permission 우회와 감사 규칙은 [인가 설계](../../.llm/auth/authorization.md)를 따른다.
- 본인 탈퇴는 별도 관계 조건을 사용한다. 동일 Space의 본인 ACTIVE/SUSPENDED Membership이면 허용하며 일반 Permission을 요구하지 않는다. OWNER는 먼저 소유권을 이전해야 한다.
- 본인 Upload·비동기 작업의 조회/취소도 Space 기본 접근 자격은 필요하다. 탈퇴·정지 후에는 과거 요청자라는 이유만으로 접근할 수 없다.
- 작업 요청자 ID는 저장된 리소스에서 확인한다. 클라이언트가 보낸 사용자 ID를 신뢰하지 않는다.

## 기본 Role matrix

| 작업 | OWNER | ADMIN | MEMBER | VIEWER |
|---|:---:|:---:|:---:|:---:|
| Space·파일 조회, preview·download | O | O | O | O |
| 파일 생성·변경, 공유·태그 관리 | O | O | O | - |
| 멤버 목록·상세 조회 | O | O | O | - |
| Space 정보 수정·보관·재활성화 | O | O | - | - |
| 초대 관리·멤버 역할 변경·정지·제거 | O | O | - | - |
| quota 변경·Space 삭제·소유권 이전 | O | - | - | - |
| 본인 Upload·비동기 작업 조회/취소 | O | O | O | O |
| 타인 Upload 조회/취소 | O | O | - | - |
| 타인 비동기 작업 조회/취소 | O | O | - | - |
| 본인 탈퇴 | - | O | O | O |

표는 기본 Role의 기본값이다. endpoint는 Role 이름이 아닌 현재 유효 Permission으로 판단한다. 요청자 본인이라는 조건은 작업 생성 Permission과 독립적이며, MEMBER에서 VIEWER로 변경되어도 기본 접근 자격이 유지되면 본인 작업을 조회/취소할 수 있다. 취소는 해당 리소스의 상태 전이에서 허용하는 경우에만 성공한다.

## Permission catalog

외부 식별자는 안정된 대문자 code다. enum 정수값과 `ToString()`에 의존하지 않는다.

| Code | Core 이름 | 책임 |
|---|---|---|
| `SPACE_VIEW` | `SpaceView` | Space 정보·quota·태그 조회 |
| `SPACE_UPDATE` | `SpaceUpdate` | Space 정보 수정·보관·재활성화 |
| `FILES_READ` | `FilesRead` | 파일·폴더·검색·휴지통 조회, preview·thumbnail |
| `FILES_WRITE` | `FilesWrite` | 파일·폴더 생성·변경, Upload·TrashOperation 생성 |
| `FILES_DOWNLOAD` | `FilesDownload` | 파일 원문 DownloadGrant 발급 |
| `SHARES_VIEW` | `SharesView` | 공유 링크 목록·상세 조회 |
| `SHARES_MANAGE` | `SharesManage` | 공유 링크 생성·변경·폐기 |
| `TAGS_MANAGE` | `TagsManage` | 태그 생성·변경·삭제와 File MANUAL tag 전체 교체 |
| `INVITES_MANAGE` | `InvitesManage` | 초대 목록·생성·폐기 |
| `MEMBERS_VIEW` | `MembersView` | Role·Membership 조회 |
| `MEMBERS_MANAGE` | `MembersManage` | 멤버 역할 변경·정지·복구·제거 |
| `UPLOADS_MANAGE` | `UploadsManage` | 같은 Space의 타인 Upload 조회/취소 |
| `OPERATIONS_MANAGE` | `OperationsManage` | 같은 Space의 타인 사용자 비동기 작업 조회/취소 |

`UPLOADS_MANAGE`, `OPERATIONS_MANAGE`는 별개 권한이다. 기본 ADMIN과 OWNER가 보유하고 MEMBER·VIEWER에는 기본 부여하지 않는다. `FILES_WRITE`로 대체하거나 자동 포함시키지 않는다. Upload 생성이나 비동기 작업 생성 권한도 부여하지 않는다. 내부 WorkerJob·DLQ 운영 권한은 `OPERATIONS_MANAGE`의 범위가 아니다.

Permission enum과 안정된 code 매핑은 Core에 정의한다. 실제 endpoint·인가 서비스 연결은 후속 구현에서 수행한다.

## Space 상태와 마스킹

- 아래 표의 상태는 Space 상태다. 리소스 자체의 lifecycle·quarantine·작업 취소 가능 상태는 Core use case가 추가 검증한다.
- `ACTIVE`: endpoint별 조건에 따라 조회·변경을 허용한다.
- `ARCHIVED`: 조회·preview·download를 허용한다. 일반 쓰기는 `409 SPACE_ARCHIVED`이며 재활성화, Space 삭제, 본인 탈퇴는 명시적 예외다.
- `DELETING`, `DELETED`: 일반 Space API에서는 `404 RESOURCE_NOT_FOUND`다.
- Space 자체가 없거나 기본 접근 자격이 없으면 endpoint 종류와 무관하게 동일한 `404 RESOURCE_NOT_FOUND`다. 설정·관리 endpoint도 이 검사를 먼저 통과해야 한다.
- `관리 403`: Space 기본 접근 자격은 있지만 해당 설정·관리 Permission 또는 OWNER 조건이 부족한 경우 `403 PERMISSION_DENIED`다.
- `리소스 404`: 콘텐츠·공유·작업의 권한 부족, 미존재, 다른 Space 리소스 주입은 같은 `404 RESOURCE_NOT_FOUND`다. status뿐 아니라 code·message·details에서도 원인을 구별하지 않는다.
- 인증 실패·actor 종류 불일치는 401, 잘못된 UUID는 400, 의존성 장애는 503이며 404로 마스킹하지 않는다.
- 이 구분은 공통 인가 단계와 endpoint metadata로 고정하고 handler가 임의로 선택하지 않는다. 내부 거부 사유와 correlation ID는 별도로 유지한다.

## Space·접근 제어 endpoint

`S = /api/v2/spaces/{spacePublicId}`, `M = S/memberships/{membershipPublicId}`.

| Method / Path | 허용 대상 | Permission 또는 관계 조건 | 허용 Space 상태 | 권한 거부 |
|---|---|---|---|---|
| GET `/api/v2/spaces` | USER | 접근 가능한 Space만 반환 | ACTIVE, ARCHIVED | 목록 필터링 |
| POST `/api/v2/spaces` | USER | 생성자가 OWNER | 신규 ACTIVE | 해당 없음 |
| GET `S` | USER | `SPACE_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| PATCH `S` | USER | `SPACE_UPDATE` | ACTIVE | 관리 403 |
| POST `S/archive` | USER | `SPACE_UPDATE` | ACTIVE | 관리 403 |
| POST `S/reactivate` | USER | `SPACE_UPDATE` | ARCHIVED | 관리 403 |
| DELETE `S` | OWNER | 소유자 확인 | ACTIVE, ARCHIVED | 관리 403 |
| GET `S/quota` | USER | `SPACE_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| PATCH `S/quota` | OWNER | 소유자 확인 | ACTIVE | 관리 403 |
| POST `S/ownership-transfers` | OWNER | 동일 Space의 활성 멤버에게 이전 | ACTIVE | 관리 403 |
| GET `S/roles` | USER | `MEMBERS_VIEW` | ACTIVE, ARCHIVED | 관리 403 |
| GET `S/memberships` | USER | `MEMBERS_VIEW` | ACTIVE, ARCHIVED | 관리 403 |
| GET `M` | USER | `MEMBERS_VIEW` | ACTIVE, ARCHIVED | 관리 403 |
| PUT `M/roles` | USER | `MEMBERS_MANAGE` | ACTIVE | 관리 403 |
| POST `M/suspend` | USER | `MEMBERS_MANAGE` | ACTIVE | 관리 403 |
| POST `M/activate` | USER | `MEMBERS_MANAGE` | ACTIVE | 관리 403 |
| DELETE `M` | USER | `MEMBERS_MANAGE` | ACTIVE | 관리 403 |
| DELETE `S/membership` | USER | 본인 ACTIVE/SUSPENDED Membership, OWNER 불가 | ACTIVE, ARCHIVED | 타인 관계는 404, OWNER 탈퇴는 403 |
| GET `S/invites` | USER | `INVITES_MANAGE` | ACTIVE, ARCHIVED | 관리 403 |
| POST `S/invites` | USER | `INVITES_MANAGE` | ACTIVE | 관리 403 |
| DELETE `S/invites/{invitePublicId}` | USER | `INVITES_MANAGE` | ACTIVE | 관리 403 |

하위 Membership·Role·Invite의 Space 불일치나 미존재는 관리 Permission이 충분해도 404다. OWNER는 Membership으로 저장하지 않으므로 멤버 관리 API로 변경·정지·제거할 수 없다. 본인 탈퇴는 동일 Space의 본인 LEFT 기록을 확인한 반복 요청도 멱등 성공하며, Membership 관계 자체가 없으면 404다. 이 제한된 반복 탈퇴 경로는 다른 Space API의 기본 접근 자격을 부여하지 않는다.

초대 공개 조회는 비로그인도 유효 capability로 접근한다. 초대 수락은 USER와 유효 capability를 요구하며 기존 Membership이나 Space Permission은 요구하지 않는다. 신규 가입·수락은 ACTIVE Space에서만 허용한다.

## 콘텐츠·전송 endpoint

이 표의 허용 대상은 모두 USER다. `S`는 위와 같다.

| Method / Path | Permission 또는 관계 조건 | 허용 Space 상태 | 권한 거부 |
|---|---|---|---|
| GET `S/folders/{folderPublicId}` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| GET `S/folders/{folderPublicId}/children` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| POST `S/folders` | `FILES_WRITE` | ACTIVE | 리소스 404 |
| PATCH `S/folders/{folderPublicId}` | `FILES_WRITE` | ACTIVE | 리소스 404 |
| GET `S/files/{filePublicId}` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| PATCH `S/files/{filePublicId}` | `FILES_WRITE` | ACTIVE | 리소스 404 |
| GET `S/search` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| POST `S/uploads` | `FILES_WRITE` | ACTIVE | 리소스 404 |
| GET `S/uploads/{uploadPublicId}` | 본인 요청 **또는** `UPLOADS_MANAGE` | ACTIVE, ARCHIVED | 리소스 404 |
| DELETE `S/uploads/{uploadPublicId}` | 본인 요청 **또는** `UPLOADS_MANAGE` | ACTIVE | 리소스 404 |
| POST `S/files/{filePublicId}/download-grants` | `FILES_DOWNLOAD` | ACTIVE, ARCHIVED | 리소스 404 |
| GET `S/files/{filePublicId}/preview` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| GET `S/files/{filePublicId}/thumbnail` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |

Upload 취소는 기존 도메인대로 CREATED/UPLOADING에서만 허용한다. FINALIZING·COMPLETED를 취소하거나 이미 완료된 File을 삭제하는 권한으로 해석하지 않는다.

## 휴지통·태그·공유 endpoint

이 표의 허용 대상은 모두 USER다. `O = S/trash-operations/{operationPublicId}`, `L = S/share-links/{shareLinkPublicId}`, `T = S/tags/{tagPublicId}`.

| Method / Path | Permission 또는 관계 조건 | 허용 Space 상태 | 권한 거부 |
|---|---|---|---|
| GET `S/trash` | `FILES_READ` | ACTIVE, ARCHIVED | 리소스 404 |
| POST `S/trash-operations` | `FILES_WRITE` | ACTIVE | 리소스 404 |
| GET `O` | 본인 요청 **또는** `OPERATIONS_MANAGE` | ACTIVE, ARCHIVED | 리소스 404 |
| 비동기 작업 취소 (상태 전이·endpoint는 후속 설계) | 본인 요청 **또는** `OPERATIONS_MANAGE` | ACTIVE | 리소스 404 |
| GET `S/tags` | `SPACE_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| GET `T` | `SPACE_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| POST `S/tags` | `TAGS_MANAGE` | ACTIVE | 리소스 404 |
| PATCH `T` | `TAGS_MANAGE` | ACTIVE | 리소스 404 |
| DELETE `T` | `TAGS_MANAGE` | ACTIVE | 리소스 404 |
| PUT `S/files/{filePublicId}/tags` | `TAGS_MANAGE` | ACTIVE | 리소스 404 |
| GET `S/share-links` | `SHARES_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| GET `L` | `SHARES_VIEW` | ACTIVE, ARCHIVED | 리소스 404 |
| POST `S/share-links` | `SHARES_MANAGE` | ACTIVE | 리소스 404 |
| PATCH `L` | `SHARES_MANAGE` | ACTIVE | 리소스 404 |
| POST `L/disable` | `SHARES_MANAGE` | ACTIVE | 리소스 404 |
| POST `L/activate` | `SHARES_MANAGE` | ACTIVE | 리소스 404 |
| DELETE `L` | `SHARES_MANAGE` | ACTIVE | 리소스 404 |

본인 작업과 타인 작업의 구분은 Core use case에서 같은 Space의 실제 리소스를 조회한 뒤 판단한다. 조회/취소 공통 권한을 확정한 것이며, 실행 중 작업의 rollback·중단 방식이나 새로운 cancel endpoint를 이 계약만으로 암묵적으로 추가하지 않는다.

계정·Session·개인 알림 API는 인증된 본인 범위, `/api/v2/admin/**`는 SYSTEM_ADMIN Session, 공개 Invite·Share·Download는 각 capability 계약을 따른다. 이 endpoint들은 Space OWNER/USER 표와 별도다.

## 검증 기준

- 설정·관리 endpoint도 비멤버·미존재 Space는 동일한 404를 반환하고, 접근 가능한 Space의 관리 권한 부족만 403이다.
- 콘텐츠·작업의 권한 거부·미존재·cross-space 주입은 같은 외부 404 형식이다.
- ARCHIVED Space에서 본인 탈퇴는 성공하고 OWNER 직접 탈퇴와 타인 Membership 탈퇴는 거부한다.
- SUSPENDED Membership의 본인 탈퇴는 허용하되 본인 Upload·작업 조회/취소에는 사용할 수 없다.
- FILES_WRITE가 없어도 기본 접근 자격이 유지되면 본인 Upload·비동기 작업의 조회/취소 관계 조건을 충족한다.
- 타인 Upload는 UPLOADS_MANAGE, 타인 비동기 작업은 OPERATIONS_MANAGE가 필요하며 두 권한은 서로 대체하지 않는다.
- FILES_WRITE만 있는 사용자는 File MANUAL tag를 교체할 수 없고 TAGS_MANAGE를 가진 사용자는 FILES_WRITE 없이도 교체할 수 있다.
- 취소 인가 성공은 취소 가능한 lifecycle 상태를 우회하지 않는다.
