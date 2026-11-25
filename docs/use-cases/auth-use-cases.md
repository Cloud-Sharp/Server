# Auth 유즈케이스

CloudSharp Production v1의 계정 등록, 로그인·로그아웃, 내 계정 관리에 해당하는 HTTP 작업 7개와 보호된 요청의 공통 Session 인증을 다룬다. [인증·사용자 API 명세](../../.llm/api/auth-users.md) · [Space 유즈케이스](space-use-cases.md)

## 구현 현황

계정 등록, 로그인, 내 프로필 조회, 비밀번호 변경과 공통 Session 인증을 API에 연결했다. 현재 Session 로그아웃, 전체 로그아웃, 내 프로필 수정은 목표 계약이며, 아래 표에 후속으로 표시했다. [Auth 라우트](../../src/CloudSharp.Api/Endpoints/Auth/AuthEndpoints.cs) · [내 계정 라우트](../../src/CloudSharp.Api/Endpoints/Users/UserEndpoints.cs)

## 공통 계약

- 등록·로그인은 익명으로 호출할 수 있다. 보호된 작업에는 `Authorization: Bearer {token}`으로 Session을 제공한다. 서버는 인증한 사용자와 Session에서 요청자 정보를 가져오며, 계정·Session API는 MCP credential을 허용하지 않는다.
- 등록·로그인 성공 응답은 `accessToken`, `tokenType=Bearer`, `idleExpiresAt`, `absoluteExpiresAt`, `user`를 반환한다. 서버는 Redis에 토큰 해시를 저장한 뒤 발급 응답에서만 원문을 반환한다. 발급 기준으로 유휴 만료는 24시간, 절대 만료는 7일이다. Redis는 사용자당 Session을 최대 10개로 유지하고 한도를 넘으면 발급 시각이 가장 오래된 Session을 제거한다. [Session 발급](../../src/CloudSharp.Core/UseCases/Auth/Sessions/SessionIssuanceService.cs) · [Redis 저장소](../../src/CloudSharp.Infrastructure/Auth/Sessions/RedisSessionStore.cs)
- 구현된 Auth·내 계정 라우트는 성공과 실패 응답에 `Cache-Control: no-store`를 적용한다. 사용자 식별자는 UUIDv7 `userPublicId`로 반환한다.
- 등록과 비밀번호 변경에서 설정하는 비밀번호는 8~128자이며 공백으로만 구성할 수 없다. 비밀번호는 공백을 잘라내거나 정규화하지 않는다.
- HTTP 본문 바인딩·검증 오류에는 공통 `REQUEST_VALIDATION_FAILED` 응답을 사용한다. 잘못된 JSON은 400, 지원하지 않는 Content-Type은 415다. 파싱된 로그인 입력의 의미 오류는 401 `AUTH_INVALID_CREDENTIALS`로 처리한다. [HTTP 계약 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/AuthHttpContractTests.cs)

## 1. 등록과 Session 관리

| ID | 유즈케이스 | API | 행위자 | 결과와 계약 | 현황 |
|---|---|---|---|---|---|
| AU-01 | [계정 등록](../../src/CloudSharp.Core/UseCases/Auth/Registrations/RegistrationUseCase.cs) | `POST /api/v2/auth/registrations` | 익명, `Idempotency-Key` 필수 | 201, 활성 계정과 첫 Session을 생성한다. `Location: /api/v2/me`를 반환한다. | 구현 |
| AU-02 | [로그인](../../src/CloudSharp.Core/UseCases/Auth/Logins/LoginUseCase.cs) | `POST /api/v2/auth/sessions` | 익명 | 200, 이메일과 비밀번호를 확인하고 새 Session을 발급한다. | 구현 |
| AU-03 | 현재 Session 로그아웃 | `DELETE /api/v2/auth/sessions/current` | Session | 204, 요청에 사용한 Session만 폐기한다. | 후속 |
| AU-04 | 전체 로그아웃 | `DELETE /api/v2/auth/sessions` | Session | 204, 사용자 `securityVersion`을 높이고 모든 Session을 폐기한다. | 후속 |

- **등록.** 이메일과 선택 입력인 사용자명의 중복을 확인하고 계정과 멱등성 완료 기록을 PostgreSQL 트랜잭션에서 저장한다. 이메일·사용자명 충돌은 각각 409 `USER_EMAIL_CONFLICT`, `USER_NAME_CONFLICT`다. 완료된 키의 같은 입력은 409 `SECRET_RESPONSE_NOT_REPLAYABLE`과 `userPublicId`를 반환한다. 커밋 후 Redis에 Session을 저장하지 못해 최초 요청이 503을 반환해도 계정은 유지되며 로그인으로 복구할 수 있다. [등록 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/RegistrationEndpointTests.cs) · [Redis 장애 테스트](../../tests/CloudSharp.Api.IntegrationTests/Failure/RedisOutageTests.cs)
- **로그인.** `loginId`는 이메일만 받으며 앞뒤 공백과 대소문자를 무시해 조회한다. 계정 없음, 비활성 계정, 비밀번호 불일치, 의미상 잘못된 입력은 모두 401 `AUTH_INVALID_CREDENTIALS`다. 로그인할 때마다 별도의 Session을 발급한다. [로그인 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/LoginEndpointTests.cs)
- **로그아웃.** 목표 계약에서 폐기 작업은 멱등하다. 이미 폐기된 토큰으로 다시 호출하면 작업에 진입하기 전에 인증이 실패할 수 있다. [로그아웃 API 명세](../../.llm/api/auth-users.md#3-로그인과-로그아웃)

## 2. 내 계정

| ID | 유즈케이스 | API | 행위자·조건 | 결과와 계약 | 현황 |
|---|---|---|---|---|---|
| US-01 | [내 프로필 조회](../../src/CloudSharp.Core/UseCases/Users/Profiles/GetProfileUseCase.cs) | `GET /api/v2/me` | Session | 200, 현재 사용자 정보와 User ETag를 반환한다. | 구현 |
| US-02 | 내 프로필 수정 | `PATCH /api/v2/me` | Session, User `If-Match` | 200, `displayName`을 변경한다. 빈 PATCH는 400이며 이메일·사용자명은 변경 대상에서 제외한다. | 후속 |
| US-03 | [비밀번호 변경](../../src/CloudSharp.Core/UseCases/Auth/PasswordChanges/PasswordChangeUseCase.cs) | `PUT /api/v2/me/password` | Session, User `If-Match`, 현재 비밀번호 확인 | 204, 새 User ETag를 반환하고 현재 Session을 유지하며 다른 Session을 폐기한다. | 구현 |

- **프로필 조회.** 인증된 사용자 ID로 PostgreSQL에서 User를 다시 읽고 활성 상태를 확인한다. 프로필과 ETag는 같은 조회 결과에서 만든다.
- **버전 검사.** 비밀번호 변경은 `"v7"`처럼 양의 User 버전을 담은 ETag 하나를 `If-Match`로 받는다. 누락은 428 `PRECONDITION_REQUIRED`, 형식 오류·와일드카드·복수 값은 400 `REQUEST_VALIDATION_FAILED`, 버전 불일치는 412 `PRECONDITION_FAILED`다. [ETag 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)
- **비밀번호 변경.** Core는 최신 계정·Session을 다시 확인한 뒤 현재 비밀번호를 검증한다. 불일치는 400 `USER_PASSWORD_MISMATCH`다. PostgreSQL에 비밀번호 해시와 증가한 보안 버전을 저장한 뒤 Redis에서 현재 Session의 보안 버전을 갱신하고 다른 Session을 원자적으로 제거한다. 현재 Session의 토큰과 만료 시각은 유지한다. 후처리 전에 현재 Session이 사라졌다면 재생성하지 않는다. [비밀번호 변경 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/PasswordChangeEndpointTests.cs)
- **커밋 후 장애.** Redis 가용성 장애로 후처리가 실패하면 새 비밀번호는 유지되고 응답은 503 `DEPENDENCY_UNAVAILABLE`이다. 서버는 현재 Session 삭제를 시도하고, 삭제 결과도 확인할 수 없으면 PostgreSQL 보안 버전 갱신을 시도한다. 두 저장소에서 모두 정리에 실패하면 즉시 무효화를 보장할 수 없다. 사용자는 새 비밀번호로 다시 로그인해 복구할 수 있다. [커밋 후 장애 테스트](../../tests/CloudSharp.Api.IntegrationTests/Failure/PasswordChangePostCommitFailureTests.cs)

## 3. 보호된 요청의 Session 인증

인증 핸들러는 Bearer 토큰을 해시해 Core에 넘긴다. Core는 해시로 Redis Session을 읽고 PostgreSQL에서 사용자를 조회한다. 사용자 PublicId와 보안 버전 일치, 활성 계정, 유휴·절대 만료를 확인한다. 인증된 요청에는 최신 `SystemRole`을 반영한다. 현재 인증 경로는 `LastSeenAt`과 유휴 만료 시각을 그대로 유지한다. [Session 인증 핸들러](../../src/CloudSharp.Api/Auth/Authentication/SessionAuthenticationHandler.cs) · [Session 인증 유즈케이스](../../src/CloudSharp.Core/UseCases/Auth/Authentication/AuthenticateSessionUseCase.cs)

| 조건 | 외부 응답 |
|---|---|
| 인증 헤더 누락 | 401 `AUTH_TOKEN_REQUIRED` |
| MCP 토큰으로 Session 전용 API 호출 | 401 `AUTH_TOKEN_TYPE_NOT_ALLOWED` |
| 잘못된 헤더나 토큰, 미존재·손상·폐기·만료 Session, 비활성 계정, 사용자 또는 보안 버전 불일치 | 401 `AUTH_TOKEN_INVALID` |
| Redis·PostgreSQL 가용성 장애 | 503 `DEPENDENCY_UNAVAILABLE` |

만료 시각에 도달한 시점부터 Session은 거부된다. [Session 인증 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/SessionAuthenticationEndpointTests.cs)

## 후속 구현

- 인증 요청 제한, Session 활동 갱신, MCP 인증을 추가한다.

Core 유닛 테스트는 계정·Session 모델과 유즈케이스를 검증한다. Docker 통합 테스트는 구현된 네 API의 HTTP 동작과 PostgreSQL·Redis 연동을 검증한다. [Auth Core 테스트 케이스](../tests/core/auth-test-cases.md) · [Auth Docker 통합 테스트 문서](../../tests/CloudSharp.Api.IntegrationTests/README.md)
