# Space Invite 테스트 케이스

[Space 유즈케이스의 IN-01~IN-05](space-use-cases.md#4-space-invite)를 검증하기 위한 시나리오다. 현재 Core 실행 골격에서 입력 검증을 확인할 수 있다. 업무 처리와 HTTP 시나리오의 기대 결과는 구현 후의 계약을 기준으로 한다.

## 공통 검증

- Core 테스트는 각 유즈케이스의 `ExecuteAsync`를 호출한다. 고정 UTC 시각 `T`, 실제 도메인 객체, 인가 정책이 만든 `AuthorizedSpaceContext`를 사용한다. 인가 이후 Space 상태가 바뀌는 경우 인가 스냅샷과 최신 저장 스냅샷을 따로 준비한다.
- 필드 검증 실패는 `ErrorCode`와 `PropertyName`을 함께 단언한다. 입력 객체 자체가 `null`이면 `ArgumentNullException`이 발생한다. 입력 검증과 행위자 유형 검사에서 거부되면 업무 조회, 토큰 발급, 쓰기를 시작하지 않는다.
- 쓰기 성공은 등록한 변경, 관찰한 이전 버전, 저장과 커밋을 각각 확인한다. 등록·저장·커밋 중 실패하면 성공 DTO 없이 실패를 반환하고, 시작한 트랜잭션의 롤백을 확인한다. 실제 데이터의 원자성과 경합은 PostgreSQL 통합 테스트에서 재조회해 검증한다.
- IN-01~IN-03에서 유효한 Session과 `INVITES_MANAGE`가 있는 OWNER·활성 멤버를 허용한다. 기본 접근 자격은 있으나 관리 권한이 없으면 Core `AUTH_SPACE_PERMISSION_DENIED`, API 403이다. 비멤버와 미존재 Space는 동일한 404다. Session이 없거나 MCP로 호출하면 401이다. Core에 직접 MCP 인가 컨텍스트를 전달하면 `AUTH_TOKEN_TYPE_NOT_ALLOWED`를 반환한다.
- IN-01~IN-03의 인가 직후 Space가 사라지면 Core는 `AUTH_SPACE_NOT_FOUND`, 허용하지 않는 상태로 바뀌면 `AUTH_SPACE_STATE_NOT_ALLOWED`를 검증한다. API는 `DELETING`·`DELETED`를 404로 마스킹하고, `ARCHIVED`에서 IN-02·IN-03을 호출하면 409 `SPACE_ARCHIVED`를 반환한다.

## IN-01 초대 목록 조회

[ListSpaceInvitesUseCase](../src/CloudSharp.Core/UseCases/Invites/Listings/ListSpaceInvitesUseCase.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| IN-01-01 | 인가된 `ACTIVE`와 `ARCHIVED` Space에서 각각 목록을 조회한다. 다른 Space의 초대도 준비한다. | 요청한 Space의 항목만 반환한다. DTO의 PublicId, 상태, 만료 시각, 최대·현재 사용 횟수, 생성·수정·폐기 시각을 확인한다. 토큰, URL, 해시, 항목 버전은 응답에 없다. |
| IN-01-02 | 결과가 없는 Space를 조회하고, 결과가 있는 Space의 마지막 페이지를 조회한다. | 빈 페이지는 `items=[]`, `nextCursor=null`이다. 마지막 페이지도 `nextCursor=null`이다. |
| IN-01-03 | 기본 요청과 `Limit=1`, `Limit=100`을 사용한다. 같은 정렬 키를 가진 항목을 페이지 경계에 두고 다음 커서로 이어서 조회한다. | 기본 한도는 50, 방향은 내림차순이다. 각 페이지가 한도를 지키고 같은 정렬 키는 PublicId로 순서를 정해 중복·누락 없이 이어진다. |
| IN-01-04 | 서명을 변조한 커서를 보내거나 다른 Space·행위자 범위·정렬에 재사용한다. | API 400 `CURSOR_INVALID`다. 응답에 타 Space 데이터가 없다. |
| IN-01-05 | `Space=null`, `Limit=0/101`, 빈 문자열·공백인 `Cursor` 또는 `Sort`를 각각 전달한다. | 차례로 `SPACE_AUTHORIZATION_CONTEXT_REQUIRED` / `Space`, `SPACE_LIST_LIMIT_INVALID` / `Limit`, `SPACE_LIST_CURSOR_INVALID` / `Cursor`, `SPACE_LIST_SORT_INVALID` / `Sort`다. |
| IN-01-06 | 목록 조회 포트의 실패를 주입한다. | 실패가 빈 페이지로 바뀌지 않는다. 초대 상태와 버전은 유지된다. |

## IN-02 초대 링크 생성

[CreateSpaceInviteUseCase](../src/CloudSharp.Core/UseCases/Invites/Creations/CreateSpaceInviteUseCase.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| IN-02-01 | 인가된 `ACTIVE` Space에서 `ExpiresAt > T`로 생성한다. `MaxUses=1`과 `MaxUses=null`을 각각 사용한다. | 요청 Space와 Session 생성자에 귀속된 UUIDv7 초대가 한 건 저장된다. 초기 상태는 `ACTIVE`, 사용 횟수 0, 버전 1이고 정책은 VIEWER 가입, 입력한 만료 시각·한도다. `null` 한도는 무제한이다. |
| IN-02-02 | 생성이 성공한 응답과 저장 데이터를 비교한다. | API 201의 PublicId·만료·한도가 저장값과 일치하고 ETag는 생성 버전을 반영한다. 토큰 원문과 해당 토큰의 URL이 최초 응답에 포함된다. 초대 레코드에는 서버가 계산한 토큰 해시만 저장된다. |
| IN-02-03 | `Space=null`, `ExpiresAt=T`, 과거 시각, 오프셋이 0이 아닌 `ExpiresAt`, `MaxUses=0/-1`을 각각 Core에 전달한다. | `SPACE_AUTHORIZATION_CONTEXT_REQUIRED` / `Space`, `INVITE_EXPIRES_AT_INVALID` / `ExpiresAt`, `INVITE_MAX_USES_INVALID` / `MaxUses`로 해당 필드를 거부한다. |
| IN-02-04 | 토큰 발급·해시 계산, 초대 등록, 저장 중 각 단계의 실패를 재현한다. | 성공 결과나 토큰·URL을 반환하지 않는다. 실패 뒤에 예정된 쓰기는 실행하지 않고, 시작한 트랜잭션을 롤백한다. |
| IN-02-05 | 첫 201 응답을 받은 뒤 같은 범위에서 같은 멱등 키와 입력으로 재요청한다. | API 409 `SECRET_RESPONSE_NOT_REPLAYABLE`이고 명세에 따라 생성된 PublicId를 반환한다. 신규 초대, 토큰 또는 URL은 발급되지 않는다. 멱등 저장소에도 토큰과 URL이 없다. |

## IN-03 초대 폐기

[RevokeSpaceInviteUseCase](../src/CloudSharp.Core/UseCases/Invites/Revocations/RevokeSpaceInviteUseCase.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| IN-03-01 | 같은 Space의 `ACTIVE` 초대를 최신 `ExpectedVersion`으로 폐기한다. 기존 가입자도 준비한다. | `REVOKED`로 전이되고 `RevokedAt`과 `UpdatedAt`은 `T`, 버전은 이전 값 + 1이다. 만료 시각, 한도, 사용 횟수, 토큰 해시와 기존 가입자의 Membership·Role은 유지된다. API는 본문 없는 204와 새 버전의 ETag를 반환한다. |
| IN-03-02 | `Space=null`, 비어 있거나 RFC variant UUIDv7이 아닌 PublicId, 0 이하의 `ExpectedVersion`을 각각 전달한다. | 차례로 `SPACE_AUTHORIZATION_CONTEXT_REQUIRED` / `Space`, `INVITE_PUBLIC_ID_INVALID` / `InvitePublicId`, `SPACE_EXPECTED_VERSION_INVALID` / `ExpectedVersion`이다. |
| IN-03-03 | `If-Match`를 빼거나, DB에서 조회한 버전보다 오래된 버전을 보낸다. | 헤더 누락은 API 428 `PRECONDITION_REQUIRED`, 조회 시 불일치는 API 412 `PRECONDITION_FAILED`다. 초대 폐기를 등록하지 않는다. |
| IN-03-04 | 처음 조회한 버전과 요청 버전은 같지만 저장 직전에 다른 요청이 초대를 갱신한다. | 관찰한 이전 버전으로 갱신을 시도한다. 저장 충돌은 412 `PRECONDITION_FAILED`로 처리하고 롤백한다. 다른 요청이 저장한 최신 상태를 덮어쓰지 않는다. |
| IN-03-05 | 없거나 다른 Space에 속한 초대의 PublicId로 폐기를 요청한다. | API 응답은 두 경우 모두 같은 404다. 다른 Space의 초대·멤버십은 변경되지 않는다. |
| IN-03-06 | 이미 `REVOKED` 또는 `EXPIRED`인 초대에 현재 버전으로 폐기를 요청한다. | Core는 `INVITE_INVALID_STATE`를 반환한다. 상태, 버전, 폐기 시각은 유지된다. |

## IN-04 공개 초대 정보 조회

[GetPublicSpaceInviteUseCase](../src/CloudSharp.Core/UseCases/Invites/PublicInfo/GetPublicSpaceInviteUseCase.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| IN-04-01 | 만료 전이고 사용 한도가 남은 초대를 Session 없이 두 번 조회한다. | 200으로 Space 이름, 같은 Space의 VIEWER Role 이름, 만료 시각만 반환한다. `Cache-Control: no-store`와 `Referrer-Policy: no-referrer`를 확인한다. 초대의 사용 횟수, 상태, 버전, 수정 시각은 유지된다. |
| IN-04-02 | 빈 토큰, 잘못된 접두사·길이·base64url 본문을 각각 Core에 전달한다. | `INVITE_TOKEN_INVALID` / `InviteToken`으로 실패하고 해시 조회를 시작하지 않는다. |
| IN-04-03 | 형식은 맞지만 미등록인 토큰, 폐기된 초대, 사용 한도 소진, `T >= ExpiresAt`, 삭제 중·삭제된 대상 Space를 각각 요청한다. | 공개 API는 응답으로 실패 원인을 구분할 수 없도록 404로 마스킹한다. Space 정보나 초대 상태가 응답에 없고 조회로 저장 상태가 변하지 않는다. |
| IN-04-04 | 정상 조회와 실패 요청의 응답·관측 데이터를 검사한다. 토큰을 담는 IN-04·IN-05 입력과 IN-02 성공 DTO의 `ToString()`도 확인한다. | 공개 조회 응답에 소유자·생성자·사용 횟수·내부 ID·토큰·URL·해시가 없다. 접근 로그의 토큰 경로 부분은 마스킹하고 trace와 metric에는 route template을 사용한다. `ToString()`은 토큰이나 토큰이 포함된 URL을 노출하지 않는다. |

## IN-05 초대 수락

[AcceptSpaceInviteUseCase](../src/CloudSharp.Core/UseCases/Invites/Acceptances/AcceptSpaceInviteUseCase.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| IN-05-01 | 기존 참여 관계가 없는 활성 Session 사용자가 `ACTIVE` Space의 유효한 초대를 수락한다. 제한 있는 초대와 무제한 초대를 각각 사용한다. | `ACTIVE` Membership을 한 건 생성하고 같은 Space의 활성 VIEWER Role만 부여한다. 초대 사용 횟수는 1 증가한다. Membership·Role과 초대 갱신이 같이 저장되고, API 200으로 두 PublicId와 `alreadyMember=false`를 반환한다. |
| IN-05-02 | 이전 Membership이 `LEFT`인 사용자가 유효한 초대로 다시 가입한다. | 이전 이력은 유지된다. 새 PublicId로 활성 Membership을 만들고 VIEWER를 부여한다. 사용 횟수가 한 번 증가하고 `alreadyMember=false`다. |
| IN-05-03 | 기존 OWNER 또는 활성 멤버가 유효한 초대를 새 멱등 키로 수락한다. | 200과 `alreadyMember=true`를 반환한다. 활성 멤버는 기존 Membership PublicId, OWNER는 Core에서 `null`이다. Membership·Role 및 초대 사용 횟수·버전·수정 시각은 유지된다. |
| IN-05-04 | 마지막 사용 슬롯을 신규 가입자가 사용한다. 이후 다른 사용자가 새 멱등 키로 수락한다. | 첫 요청은 성공하고 초대는 사용 한도에 도달해 `EXPIRED`가 된다. 후속 요청은 Core에서 `INVITE_USAGE_EXHAUSTED`로 실패하며 추가 가입이나 횟수 증가가 없다. |
| IN-05-05 | 신규 가입자에 대해 폐기·한도 소진·만료가 겹치는 상태와 만료 경계 `T=ExpiresAt`를 준비한다. | Core는 `INVITE_REVOKED` → `INVITE_USAGE_EXHAUSTED` → `INVITE_EXPIRED` 순으로 판정한다. 만료 시각과 같으면 이미 만료된 상태로 처리한다. 실패 시 Membership·Role·초대는 그대로다. |
| IN-05-06 | `Actor=null`, 잘못된 Session actor, MCP, 형식 오류 토큰을 각각 Core에 전달한다. | `SPACE_ACTOR_REQUIRED` / `Actor`, `AUTH_TOKEN_INVALID`, `AUTH_TOKEN_TYPE_NOT_ALLOWED`, `INVITE_TOKEN_INVALID` / `InviteToken`을 확인한다. |
| IN-05-07 | 비로그인 또는 MCP, 최신 계정이 정지·삭제된 사용자, 미등록 토큰, `ARCHIVED`·`DELETING`·`DELETED` 대상 Space, 다른 Space에만 활성 VIEWER가 있는 경우를 각각 재현한다. | 가입·Role 등록·사용 횟수 증가는 모두 없어야 한다. API에서 유효한 Session 부재는 401, `ARCHIVED` 쓰기는 409 `SPACE_ARCHIVED`, `DELETING`·`DELETED`는 404다. |
| IN-05-08 | 유효한 초대를 기존 `SUSPENDED` 멤버가 수락한다. | 초대로 정지를 우회할 수 없다. 기존 Membership과 Role을 복구하거나 교체하지 않고, 새 Membership 생성과 사용 횟수 증가도 없다. |
| IN-05-09 | Membership·Role 등록, 초대 갱신, 저장 또는 커밋 중 실패를 각각 주입한다. | 오류를 반환하고 시작한 트랜잭션을 롤백한다. 별도 조회에서 새 멤버·Role 연결·사용 횟수·초대 버전 중 일부만 반영된 상태가 없음을 확인한다. |
| IN-05-10 | 한 슬롯을 두 사용자가 동시에 수락한다. 별도로 한 사용자가 사용 여유가 있는 초대를 다른 키로 동시에 수락한다. | 첫 경우 가입 한 건과 사용 횟수 증가 한 번만 커밋된다. 다른 요청은 사용 한도를 초과하지 못한다. 둘째 경우 같은 사용자에 대한 활성 Membership과 사용 횟수 증가는 각각 한 건뿐이다. |
| IN-05-11 | 수락 200 이후 같은 범위에서 같은 키와 입력으로 재요청한다. 첫 성공에 마지막 슬롯을 사용한 경우도 포함한다. | 최초 200의 Membership PublicId와 `alreadyMember`를 그대로 재생한다. 추가 Membership·Role이 생성되거나 사용 횟수가 증가하지 않는다. |

## IN-02·IN-05 공통 HTTP 멱등성

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 공통-01 | 키를 생략하거나 16~128자 범위를 벗어난 키 또는 비 ASCII 값을 보낸다. | 요청 검증에서 거부하며 업무 쓰기를 시작하지 않는다. |
| 공통-02 | 첫 요청을 처리하는 중 같은 actor·method·canonical path·키·입력으로 재요청한다. | API 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS`다. 업무 처리가 중복 실행되지 않는다. |
| 공통-03 | 같은 범위와 키에 다른 fingerprint를 사용한다. 예를 들어 IN-02의 만료 시각을 바꾼다. | API 409 `IDEMPOTENCY_KEY_REUSED`다. actor 또는 canonical path가 다른 요청은 같은 문자열 키로 기존 결과를 공유하지 않는다. |
| 공통-04 | 멱등 저장소를 사용할 수 없는 상태에서 요청한다. | API 503 `DEPENDENCY_UNAVAILABLE`로 실패한다. 멱등성 확인 없이 초대를 만들거나 수락하지 않는다. |

## 기대값 확정이 필요한 항목

- IN-01의 정렬 필드 허용 목록과 기본 정렬 필드, 초대 상태별 목록 포함 정책.
- IN-03에서 생성 후 초대 버전이 바뀐 경우 최신 ETag를 얻는 경로, 만료 시각이 지났지만 저장 상태는 `ACTIVE`인 초대의 폐기 정책.
- IN-04에서 유효 토큰이 가리키는 `ARCHIVED` Space의 공개 조회 허용 여부, 마스킹 응답의 외부 오류 코드.
- IN-05에서 기존 OWNER의 `membershipPublicId`를 HTTP에서 `null`로 보낼지 생략할지, `SUSPENDED` 멤버와 VIEWER 누락의 외부 오류, 초대 실패 사유의 HTTP 매핑. 새 멱등 키로 이미 가입한 사용자가 유효하지 않은 초대를 수락할 때는 기존 가입 여부와 초대 유효성의 검사 우선순위도 확정해야 한다.

[API 세부 계약](../.llm/api/spaces-access.md#5-invite) · [공통 API 계약](../.llm/api/common-contract.md) · [초대 도메인](../.llm/domains/space-invite.md) · [인가 계약](../contracts/permissions/README.md)
