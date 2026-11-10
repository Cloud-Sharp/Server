# Space 유즈케이스

CloudSharp Production v1의 Space 관리와 접근 제어를 구현하기 위한 목록이다. `.llm/api/spaces-access.md`의 API 23개를 **Space 10개, Role 1개, Membership 7개, Invite 5개**로 분류했다.

파일·폴더·검색·전송, 태그·휴지통, 공유 링크, 시스템 관리자 작업은 각각 `.llm/api/content-transfer.md`, `.llm/api/trash-tags.md`, `.llm/api/sharing-public.md`, `.llm/api/admin-internal.md`에서 다룬다. 이 문서에는 Space 생성·삭제와 같이 다른 도메인과 함께 처리해야 하는 경계만 포함한다.

## 공통 계약

- `S`는 `/api/v2/spaces/{spacePublicId}`, `M`은 `S/memberships/{membershipPublicId}`, `I`는 `S/invites/{invitePublicId}`를 뜻한다. API의 리소스 식별자는 UUIDv7 PublicId다.
- 기본 행위자는 활성 계정의 Session 사용자다. MCP는 Space 목록·상세·quota 조회에 허용된다. 조회 결과와 유효 권한은 credential의 Space allowlist, scope, 발급자에게 현재 부여된 권한을 적용해 산정한다. 단건 상세·quota 조회에는 `spaces:read` scope가 필요하다.
- Session의 일반 접근 자격은 OWNER 또는 활성 Membership이다. 멤버의 유효 Permission은 연결된 활성 Role들의 Permission 합집합이다. Session `SYSTEM_ADMIN`의 Permission 우회는 감사 대상이며 OWNER 전용 조건은 별도로 검증한다. [권한 계약](../contracts/permissions/README.md)
- Space와 하위 리소스의 미존재, 기본 접근 자격 부족, 다른 Space의 리소스 ID 주입은 외부 응답에서 404로 마스킹한다. 기본 접근 자격이 있는 사용자의 설정·관리 권한 부족은 403이다. 일반 Space API에서 `DELETING`·`DELETED`는 404, `ARCHIVED`의 일반 쓰기는 409 `SPACE_ARCHIVED`다.
- 표에 `If-Match`가 표시된 변경은 대상 Aggregate의 최신 ETag를 요구한다. 헤더 누락은 428, 버전 불일치는 412다. cursor가 필요한 목록은 `items`와 `nextCursor`를 반환한다.
- `Idempotency-Key`가 필요한 요청은 같은 키와 입력으로 재시도해도 변경을 중복 적용하지 않는다. 같은 키에 다른 입력은 409다. 초대 생성처럼 비밀값을 한 번만 반환하는 요청은 비밀값을 재생하는 대신 409 `SECRET_RESPONSE_NOT_REPLAYABLE`을 반환한다.

## 1. Space

Space가 소유자, 메타데이터, 상태, 용량 집계를 관리한다. quota와 소유권은 같은 도메인에 속한다.

| ID | 유즈케이스 | API | 행위자·조건 | 결과와 계약 |
|---|---|---|---|---|
| SP-01 | 접근 가능한 Space 목록 조회 | `GET /api/v2/spaces` | Session/MCP, 접근 가능한 `ACTIVE`·`ARCHIVED` | 200, 접근 가능한 항목을 cursor로 반환한다. |
| SP-02 | Space 생성 | `POST /api/v2/spaces` | Session, `Idempotency-Key` | 201, 이름·설명·허용 용량으로 새 `ACTIVE` Space를 생성하고 요청자를 OWNER로 지정한다. |
| SP-03 | Space 상세 조회 | `GET S` | Session/MCP, `SPACE_VIEW`, `ACTIVE`·`ARCHIVED` | 200, Space 정보와 루트 폴더 ID, 요청자의 `myAccess`, ETag를 반환한다. |
| SP-04 | Space 정보 수정 | `PATCH S` | `SPACE_UPDATE`, `ACTIVE`, `If-Match` | 200, 이름과 설명을 갱신한다. |
| SP-05 | Space 보관 | `POST S/archive` | `SPACE_UPDATE`, `ACTIVE`, `If-Match` | 200, `ARCHIVED`로 전환해 조회를 유지하고 일반 변경을 막는다. |
| SP-06 | Space 재활성화 | `POST S/reactivate` | `SPACE_UPDATE`, `ARCHIVED`, `If-Match` | 200, `ACTIVE`로 전환해 쓰기 가능한 상태로 되돌린다. |
| SP-07 | Space 삭제 요청 | `DELETE S` | OWNER, `ACTIVE`·`ARCHIVED`, `If-Match` | 202, `DELETING`으로 전환하고 ID·상태·접수 시각을 반환한다. |
| SP-08 | Space quota 조회 | `GET S/quota` | Session/MCP, `SPACE_VIEW`, `ACTIVE`·`ARCHIVED` | 200, 허용·사용·예약·가용 용량과 Space ETag를 반환한다. |
| SP-09 | Space quota 변경 | `PATCH S/quota` | OWNER, `ACTIVE`, Space `If-Match` | 200, 허용 용량을 변경한다. `null`은 무제한이다. 사용량과 예약량의 합보다 낮으면 409 `SPACE_QUOTA_TOO_SMALL`이다. |
| SP-10 | Space 소유권 이전 | `POST S/ownership-transfers` | OWNER, `ACTIVE`, Space `If-Match` | 200, 같은 Space의 활성 Membership을 가진 활성 계정으로 소유권을 이전한다. |

- **생성.** 사용량과 예약량은 0으로 시작한다. Space, 루트 DirectoryEntry·Folder, 기본 ADMIN·MEMBER·VIEWER Role과 Permission 연결을 한 트랜잭션에 저장한다. OWNER는 `Space.OwnerUserId`에 기록한다.
- **삭제.** 202 응답 이후 종속 리소스를 정리한다. 정리가 끝나고 사용량과 예약량이 모두 0인 경우에만 `DELETED`로 전환한다. 이 후속 처리는 별도의 사용자 API 없이 수행한다.
- **소유권 이전.** 대상이 현재 OWNER이면 변경 없이 성공한다. 다른 사용자에게 이전할 때는 새 OWNER의 Membership과 Role 연결 제거, 기존 OWNER의 ADMIN Membership 생성, Space 소유자 변경을 한 트랜잭션에서 처리한다.

## 2. Space Role

Space별 Role 정의와 Permission을 조회한다. Production v1의 관리형 Role은 Space 생성 시 준비하며, 멤버에게 어떤 Role을 할당할지는 Membership이 관리한다.

| ID | 유즈케이스 | API | 행위자·조건 | 결과와 계약 |
|---|---|---|---|---|
| RL-01 | Space Role 목록 조회 | `GET S/roles` | `MEMBERS_VIEW`, `ACTIVE`·`ARCHIVED` | 200, 관리형 Role의 PublicId·코드·이름·Permission을 반환한다. |

## 3. Space Membership

Space 참여 상태와 Role 할당을 관리한다. OWNER는 Space에 기록하며 멤버 목록과 멤버 변경의 대상에서 제외한다.

| ID | 유즈케이스 | API | 행위자·조건 | 결과와 계약 |
|---|---|---|---|---|
| MB-01 | 멤버 목록 조회 | `GET S/memberships` | `MEMBERS_VIEW`, `ACTIVE`·`ARCHIVED` Space | 200, 멤버의 사용자 정보·상태·Role을 cursor로 반환한다. |
| MB-02 | 멤버 상세 조회 | `GET M` | `MEMBERS_VIEW`, `ACTIVE`·`ARCHIVED` Space | 200, 멤버 정보와 Membership ETag를 반환한다. |
| MB-03 | 멤버 Role 전체 교체 | `PUT M/roles` | `MEMBERS_MANAGE`, `ACTIVE` Space, Membership `If-Match` | 200, `ACTIVE`·`SUSPENDED` 멤버의 Role 집합을 교체한다. |
| MB-04 | 멤버 정지 | `POST M/suspend` | `MEMBERS_MANAGE`, `ACTIVE` Space, Membership `If-Match` | 200, `ACTIVE → SUSPENDED`로 전환해 일반 Space 접근을 차단한다. |
| MB-05 | 정지 멤버 활성화 | `POST M/activate` | `MEMBERS_MANAGE`, `ACTIVE` Space, Membership `If-Match` | 200, Role이 하나 이상 있는 `SUSPENDED` 멤버를 `ACTIVE`로 전환한다. |
| MB-06 | 멤버 제거 | `DELETE M` | `MEMBERS_MANAGE`, `ACTIVE` Space, Membership `If-Match` | 204, 대상 `ACTIVE`·`SUSPENDED` Membership을 `LEFT`로 전환한다. |
| MB-07 | 본인 탈퇴 | `DELETE S/membership` | 동일 Space의 본인 `ACTIVE`·`SUSPENDED` Membership, `ACTIVE`·`ARCHIVED` Space | 204, 본인 Membership을 `LEFT`로 전환한다. 본인의 기존 `LEFT` 기록이 있으면 반복 요청도 204다. |

- **Role 교체.** `rolePublicIds`에는 같은 Space의 활성 Role이 하나 이상 있어야 하며 중복을 허용하지 않는다. 유지한 Role의 부여 이력은 보존하고, 순서만 다른 같은 집합은 변경 없이 성공한다.
- **제거와 탈퇴.** `LEFT`는 최종 상태이며 재가입 시 새 Membership을 생성한다. 본인 탈퇴는 일반 Space Permission 없이 저장된 관계를 확인한다. 관계가 없으면 404, OWNER가 직접 탈퇴하면 403이다. 탈퇴 반복 요청의 예외는 해당 API에서만 적용한다.

## 4. Space Invite

초대 링크의 발급·조회·폐기와 수락을 관리한다. 신규 가입 Role은 VIEWER로 고정한다.

| ID | 유즈케이스 | API | 행위자·조건 | 결과와 계약 |
|---|---|---|---|---|
| IN-01 | 초대 목록 조회 | `GET S/invites` | `INVITES_MANAGE`, `ACTIVE`·`ARCHIVED` Space | 200, 초대 관리 정보를 cursor로 반환한다. token과 URL은 응답에서 제외한다. |
| IN-02 | 초대 링크 생성 | `POST S/invites` | `INVITES_MANAGE`, `ACTIVE` Space, `Idempotency-Key` | 201, 만료 시각·최대 사용 횟수로 링크를 발급하고 ETag를 반환한다. token과 URL은 생성 시 한 번 제공한다. |
| IN-03 | 초대 폐기 | `DELETE I` | `INVITES_MANAGE`, `ACTIVE` Space, Invite `If-Match` | 204, 관리용 PublicId로 초대를 폐기한다. 이미 가입한 멤버의 권한은 유지된다. |
| IN-04 | 공개 초대 정보 조회 | `GET /public/v2/invites/{inviteToken}` | 익명 가능, 유효한 초대 token | 200, Space 이름·가입 Role 이름·만료 시각만 `no-store`로 반환한다. |
| IN-05 | 초대 수락 | `POST /api/v2/invites/{inviteToken}/accept` | Session, 유효한 초대 token, `ACTIVE` Space, `Idempotency-Key` | 200, Space ID와 가입 결과를 반환한다. 신규 가입자의 Membership ID와 `alreadyMember=false`를 반환한다. |

- **생성.** 만료 시각은 현재보다 미래여야 한다. 최대 사용 횟수는 양수 또는 `null`이며 `null`은 제한 없음이다. 서버에는 token hash만 저장한다.
- **수락.** 활성 계정을 확인하고 같은 Space의 VIEWER Role을 사용한다. 신규 Membership 생성과 초대 사용 횟수 증가는 한 트랜잭션으로 처리하고 동시 요청도 사용 한도를 넘을 수 없게 한다. 기존 OWNER나 활성 멤버의 반복 수락은 `alreadyMember=true`로 성공하며 Role이나 사용 횟수를 변경하지 않는다.
- **실패 분기.** 초대의 수락 가능 여부는 `INVITE_REVOKED` → `INVITE_USAGE_EXHAUSTED` → `INVITE_EXPIRED` 순으로 검사한다. 마지막 허용 수락이 성공하면 초대가 `EXPIRED`로 전환된다.

## API 계약에서 추가로 정할 항목

- **초대 폐기용 최신 ETag 조회.** 폐기에는 `If-Match`가 필수다. 현재 명세는 생성 응답의 ETag만 명시하며, 초대 목록에는 공통 계약상 항목별 버전이 없고 관리용 단건 조회 API도 정의되어 있지 않다. 초대 사용으로 버전이 바뀐 뒤에도 최신 값을 얻을 수 있도록 조회 계약을 정해야 한다.
- **초대 수락의 경계 응답.** OWNER의 반복 수락은 성공하지만 OWNER에게 Membership이 없으므로 `membershipPublicId`의 null·생략 여부를 정해야 한다. 이미 `SUSPENDED` Membership을 가진 사용자가 수락을 시도할 때의 외부 오류도 정해야 한다.

API와 도메인 세부 근거는 로컬 설계의 `.llm/api/common-contract.md`, `.llm/domains/space.md`, `.llm/domains/space-role.md`, `.llm/domains/space-membership.md`, `.llm/domains/space-invite.md`, `.llm/database/spaces-access.md`에 있다.
