# Auth API

`/api/v2/auth`에 등록된 계정 등록과 Session 발급 API다. 두 API 모두 익명으로 호출할 수 있다. [라우트 구현](../../src/CloudSharp.Api/Endpoints/Auth/AuthEndpoints.cs) · [Auth 유즈케이스](../use-cases/auth-use-cases.md#1-등록과-session-관리) · [Auth API 통합 테스트 케이스](../tests/api-integration/auth-api-integration-test-cases.md)

| ID | API | 메서드·경로 | 요청 | 성공 응답 |
|---|---|---|---|---|
| AU-01 | [계정 등록](#au-01-계정-등록) | `POST /api/v2/auth/registrations` | `Idempotency-Key`, JSON `email`, `password`, 선택 `userName`·`displayName` | 201, `Location: /api/v2/me`, Session 발급 결과 |
| AU-02 | [로그인](#au-02-로그인) | `POST /api/v2/auth/sessions` | JSON `loginId`, `password` | 200, Session 발급 결과 |

## 공통 응답

- 발급 결과는 `accessToken`, `tokenType` (`Bearer`), `idleExpiresAt`, `absoluteExpiresAt`, `user`를 담는다. 반환된 토큰은 [User API](user-api.md)에서 Bearer 인증에 사용한다. `user`의 필드는 [프로필 응답](user-api.md#us-01-내-프로필-조회)과 같다. [응답 계약](../../src/CloudSharp.Api/Endpoints/Auth/Responses/AuthSessionResponse.cs)
- Session의 유휴·절대 만료는 발급 시각 기준 24시간·7일이다. Redis에는 토큰 해시를 저장하고, 사용자당 발급 시각이 최신인 Session을 최대 10개 유지한다. [Session 계약](../use-cases/auth-use-cases.md#공통-계약)
- 성공과 오류 응답에 `Cache-Control: no-store`를 적용한다. 잘못된 JSON 문법·필드 자료형·미정의 속성은 400, 지원하지 않는 미디어 타입은 415이며 오류 코드는 `REQUEST_VALIDATION_FAILED`다. 오류 본문의 `requestId`는 응답의 `X-Correlation-ID`와 같다. [HTTP 계약 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/AuthHttpContractTests.cs)

## AU-01 계정 등록

- 필수 `email`은 이메일 형식으로 최대 320자다. 선택 `userName`과 `displayName`은 각각 최대 30자, 100자다. 필수 `password`는 8~128자이며 공백만으로 구성할 수 없다. 비밀번호의 앞뒤 공백은 보존한다. [등록 요청 DTO](../../src/CloudSharp.Api/Endpoints/Auth/Requests/RegistrationRequest.cs)
- 성공 시 활성 계정을 저장하고 첫 Session을 발급한다. 이메일·사용자명 중복은 각각 409 `USER_EMAIL_CONFLICT`, `USER_NAME_CONFLICT`다. [등록 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/RegistrationEndpointTests.cs)
- `Idempotency-Key`는 출력 가능한 ASCII 16~128자의 단일 값으로 필수다. 익명 요청 범위에는 앞뒤 공백과 대소문자를 정규화한 이메일의 HMAC을 사용한다. 계정과 멱등성 완료 기록을 같은 PostgreSQL 트랜잭션에 저장한다. [공통 멱등성 필터 설계](http-idempotency-filter-design.md)
- 커밋 후 24시간 동안 같은 키와 입력으로 재요청하면 409 `SECRET_RESPONSE_NOT_REPLAYABLE`과 최상위 `userPublicId`를 반환한다. 같은 범위의 키에 다른 입력을 보내면 409 `IDEMPOTENCY_KEY_REUSED`다. [등록 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/RegistrationEndpointTests.cs)
- 최초 계정 저장 후 Redis Session 발급에 실패하면 503 `DEPENDENCY_UNAVAILABLE`이다. 해당 키의 재요청은 커밋된 계정의 `userPublicId`를 반환하며, 클라이언트는 로그인으로 Session을 발급받을 수 있다. [Redis 장애 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Failure/RedisOutageTests.cs)

## AU-02 로그인

- `loginId`는 이메일이며 앞뒤 공백과 대소문자를 무시해 조회한다. `password`는 제출된 값으로 검증한다. 성공할 때마다 별도 Session을 발급한다. [로그인 요청 DTO](../../src/CloudSharp.Api/Endpoints/Auth/Requests/LoginRequest.cs)
- JSON 바인딩을 통과한 입력의 의미 오류, 미존재·정지·삭제 계정, 비밀번호 불일치는 모두 401 `AUTH_INVALID_CREDENTIALS`다. 저장소 가용성 장애는 503 `DEPENDENCY_UNAVAILABLE`이다. [로그인 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/LoginEndpointTests.cs)
