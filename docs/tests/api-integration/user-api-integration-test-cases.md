# User API 통합 테스트 케이스

[User API](../../api/user-api.md)의 내 프로필 조회(`GET /api/v2/me`)와 비밀번호 변경(`PUT /api/v2/me/password`)을 검증하는 테스트다. 등록·로그인에서 받은 실제 Session으로 HTTP 요청을 보낸다. Docker 테스트 기반과 공통 오류 검증은 [Auth API 통합 테스트 케이스](auth-api-integration-test-cases.md#공통-검증)에 정리되어 있다.

## US-01 내 프로필 조회와 Session 인증

[SessionAuthenticationEndpointTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/SessionAuthenticationEndpointTests.cs) · [PostgresOutageTests](../../../tests/CloudSharp.Api.IntegrationTests/Failure/PostgresOutageTests.cs) · [RedisOutageTests](../../../tests/CloudSharp.Api.IntegrationTests/Failure/RedisOutageTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| US-01-01 | 서로 다른 계정 두 개를 등록하고 한 계정의 Session으로 프로필을 조회한다. | 200 JSON과 `Cache-Control: no-store`, User ETag `"v1"`을 반환한다. 본인 UUIDv7 PublicId, 프로필 필드, UTC 시각을 확인한다. 다른 계정의 이메일·PublicId, 내부 식별 정보, 비밀번호는 노출되지 않는다. |
| US-01-02 | DB의 사용자를 SYSTEM_ADMIN으로 바꾼 뒤 같은 Session으로 조회한다. | 새 토큰 발급 없이 최신 `SYSTEM_ADMIN` 역할과 ETag `"v2"`가 반영된다. |
| US-01-03 | Authorization 헤더를 빼거나 Bearer 이외의 scheme, 알 수 없는 토큰 접두사, 위조 Session 토큰, 길이 초과 토큰, 중복 헤더를 보낸다. | 누락은 401 `AUTH_TOKEN_REQUIRED`, 잘못된 헤더나 토큰은 401 `AUTH_TOKEN_INVALID`다. 누락 및 단일 헤더 형식 오류 케이스는 `WWW-Authenticate: Bearer`도 확인한다. |
| US-01-04 | MCP credential 형식의 토큰을 보낸다. | 401 `AUTH_TOKEN_TYPE_NOT_ALLOWED`와 `WWW-Authenticate: Bearer`를 반환한다. |
| US-01-05 | Redis의 Session 본문을 삭제하거나 보안 버전 누락·0, 빈 SessionId, 잘못된 만료 시각 순서로 본문을 손상한다. | 401 `AUTH_TOKEN_INVALID`다. 손상된 본문은 조회 후 Redis에서 제거된다. |
| US-01-06 | 테스트 시계를 유휴·절대 만료 1초 전과 만료 시각으로 설정해 각각 조회한다. | 1초 전에는 정상 프로필이 반환되고 만료 시각에 도달하면 401 `AUTH_TOKEN_INVALID`다. |
| US-01-07 | 발급 후 DB의 보안 버전을 올리거나 사용자 상태를 정지·삭제로 바꾼다. | 기존 Session으로 조회하면 401 `AUTH_TOKEN_INVALID`다. |
| US-01-08 | 정상 프로필 조회를 확인한 뒤 PostgreSQL 또는 Redis를 중단하고 같은 Session으로 조회한다. | 두 장애 모두 503 `DEPENDENCY_UNAVAILABLE`이다. 오류 본문에 Session 토큰이 없다. |

## US-03 비밀번호 변경

[PasswordChangeEndpointTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/PasswordChangeEndpointTests.cs) · [AuthHttpContractTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/AuthHttpContractTests.cs) · [PasswordChangePostCommitFailureTests](../../../tests/CloudSharp.Api.IntegrationTests/Failure/PasswordChangePostCommitFailureTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| US-03-01 | 같은 사용자로 Session 두 개를 발급한 뒤, 한 Session과 최신 `If-Match` 및 올바른 현재 비밀번호로 변경한다. | 204, 빈 본문, 새 ETag `"v2"`를 반환한다. PostgreSQL의 해시·수정 시각이 갱신되고 User 버전·보안 버전이 2가 된다. 현재 Redis Session의 보안 버전은 2이고 다른 Session 본문은 제거된다. 현재 토큰은 프로필을 조회할 수 있으며 다른 토큰은 401 `AUTH_TOKEN_INVALID`다. 기존 비밀번호는 401 `AUTH_INVALID_CREDENTIALS`, 새 비밀번호는 로그인에 성공한다. |
| US-03-02 | `If-Match` 없이 비밀번호를 변경한다. | 428 `PRECONDITION_REQUIRED`다. DB에 저장된 비밀번호 해시와 두 버전은 변경 전 값이다. |
| US-03-03 | 와일드카드, 따옴표 누락, 0 버전, 약한 ETag, 잘못된 접두사, 한 헤더의 복수 값 또는 중복 `If-Match` 헤더를 보낸다. | 모두 400 `REQUEST_VALIDATION_FAILED`다. 잘못된 값 케이스는 `ifMatch` / `INVALID_ETAG` 상세를 확인한다. 비밀번호 해시와 두 버전은 변경 전 값이다. |
| US-03-04 | 조회한 ETag보다 DB 버전이 높아진 상태에서 이전 ETag로 변경한다. | 412 `PRECONDITION_FAILED`다. 비밀번호와 보안 버전을 보존하고 최신 User 버전을 덮어쓰지 않는다. 현재 Session으로 프로필을 조회할 수 있다. |
| US-03-05 | 잘못된 현재 비밀번호를 전달한다. | 400 `USER_PASSWORD_MISMATCH`다. 오류 본문에 기존·새 비밀번호가 없고 저장된 해시와 두 버전을 보존한다. |
| US-03-06 | 새 비밀번호에 길이 미달 값이나 빈 문자열을 전달한다. | 400 `REQUEST_VALIDATION_FAILED`이며 상세에 `newPassword` / `INVALID`가 있다. 저장된 해시와 두 버전은 변경 전 값이다. |
| US-03-07 | 유효한 Session과 ETag로 깨진 JSON, 알 수 없는 필드 또는 숫자형 `currentPassword`를 전달한다. | 400 `REQUEST_VALIDATION_FAILED`이며 `error.details`는 빈 배열이다. DB의 기존 해시로 원래 비밀번호를 계속 검증할 수 있다. |
| US-03-08 | 커밋 인터셉터를 활성화한 뒤 프로필을 조회하고 비밀번호를 변경한다. 쓰기 커밋 직후 Redis가 중단되면 재시작해 기존 토큰과 두 비밀번호를 검증한다. | 읽기 트랜잭션에서는 Redis가 연결된 상태를 유지한다. 변경 응답은 503 `DEPENDENCY_UNAVAILABLE`이다. 복구 후 Redis에 기존 두 본문이 남아 있어도 두 토큰은 401 `AUTH_TOKEN_INVALID`다. DB에는 새 비밀번호가 남고, 이전 비밀번호는 401 `AUTH_INVALID_CREDENTIALS`, 새 비밀번호는 같은 계정으로 로그인에 성공한다. |
