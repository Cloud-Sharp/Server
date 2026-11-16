# Auth API 통합 테스트 케이스

[Auth API](../../api/auth-api.md)의 계정 등록(`POST /api/v2/auth/registrations`)과 로그인(`POST /api/v2/auth/sessions`)을 검증하는 테스트다. Session으로 보호된 프로필 조회와 비밀번호 변경은 [User API 통합 테스트 케이스](user-api-integration-test-cases.md)에서 다룬다.

## 공통 검증

- `WebApplicationFactory<Program>`으로 Production 환경의 HTTP 호스트를 시작하고, Testcontainers의 PostgreSQL과 Redis를 연결한다. 일반 픽스처는 각 테스트 전에 두 저장소를 초기화한다. 장애 픽스처는 전용 컨테이너와 호스트를 사용한다. 실행 방법과 이전 실행 결과는 [API 통합 테스트 README](../../../tests/CloudSharp.Api.IntegrationTests/README.md)에 있다.
- 공통 오류 단언은 HTTP 상태, JSON 미디어 타입, `Cache-Control: no-store`, `error.code`와 `error.details`를 확인한다. 본문의 `requestId`는 응답의 `X-Correlation-ID`와 일치한다. [IntegrationAssert](../../../tests/CloudSharp.Api.IntegrationTests/TestHost/IntegrationAssert.cs)

## 공통 HTTP 계약

[AuthHttpContractTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/AuthHttpContractTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| HTTP-01 | 등록·로그인에 깨진 JSON, 알 수 없는 필드 또는 문자열이어야 할 필드에 숫자를 보낸다. 등록 본문이 JSON `null`인 경우도 확인한다. | 400 `REQUEST_VALIDATION_FAILED`이며 `error.details`는 빈 배열이다. |
| HTTP-02 | 등록 요청의 Content-Type을 `text/plain`으로 보낸다. | 415 `REQUEST_VALIDATION_FAILED`이며 `error.details`는 빈 배열이다. |
| HTTP-03 | 유효한 `X-Correlation-ID`를 포함해 중복 등록을 요청한다. | 409 응답의 헤더와 본문의 `requestId`가 요청 ID와 일치한다. |
| HTTP-04 | 로그인 오류 요청에 빈 값, 허용되지 않은 문자, 128자 초과 또는 중복 상관 ID 헤더를 사용한다. | 서버가 소문자 16진수 32자리 ID를 생성하고 응답 헤더와 오류 본문에 같은 값을 사용한다. |
| HTTP-05 | 등록 성공 응답의 JSON을 읽는다. | camelCase 필드, `tokenType=Bearer`, 발급 만료 시각의 문자열, `USER`·`ACTIVE` 문자열과 UUIDv7 `userPublicId`를 검증한다. `user`는 정의된 공개 필드만 담는다. |

## AU-01 계정 등록

[RegistrationEndpointTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/RegistrationEndpointTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| AU-01-01 | 유효한 이메일·사용자명·비밀번호와 양끝 공백이 있는 표시 이름으로 등록한다. 응답 토큰으로 `GET /api/v2/me`를 호출한다. | 201, `Location: /api/v2/me`, Bearer Session과 발급 시각 기준 24시간·7일 만료를 반환한다. PostgreSQL에 정규화 값, 공백을 제거한 표시 이름, 검증 가능한 비밀번호 해시, 버전·보안 버전 1이 저장된다. Redis Session의 사용자·보안 버전이 일치하고 인덱스에 토큰 해시가 있다. 응답은 비밀번호·해시를 노출하지 않으며 프로필의 ETag는 `"v1"`이다. |
| AU-01-02 | 선택 필드인 사용자명과 표시 이름을 생략한다. | 응답과 DB에서 두 필드가 `null`이다. DB의 정규화 사용자명도 `null`이다. |
| AU-01-03 | 이메일 형식 오류·누락 또는 비밀번호 길이 미달·누락을 각각 보낸다. | 400 `REQUEST_VALIDATION_FAILED`이며 해당 `email` 또는 `password`의 상세 코드가 `INVALID`다. |
| AU-01-04 | 기존 이메일 또는 사용자명의 대소문자를 바꿔 새 등록을 요청한다. | 각각 409 `USER_EMAIL_CONFLICT`, `USER_NAME_CONFLICT`다. DB 사용자 수와 기존 사용자의 Redis Session 수를 보존한다. |
| AU-01-05 | 같은 이메일로 두 요청을 동시에 보낸다. | 201과 409 `USER_EMAIL_CONFLICT`가 각각 한 건이다. DB 사용자와 Redis Session 인덱스 항목도 각각 한 건이다. |

## AU-02 로그인

[LoginEndpointTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/LoginEndpointTests.cs) · [SessionAuthenticationEndpointTests](../../../tests/CloudSharp.Api.IntegrationTests/Auth/SessionAuthenticationEndpointTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| AU-02-01 | 등록한 이메일의 대소문자를 바꾸고 앞뒤 공백을 넣어 로그인한다. | 200으로 같은 사용자의 공개 정보, 별도 Bearer 토큰과 발급 기준 24시간·7일 만료를 반환한다. Redis에 등록 때의 Session과 새 Session이 모두 남아 있다. 응답에 비밀번호나 내부 식별 정보가 없다. |
| AU-02-02 | 존재하지 않는 계정, 잘못된 비밀번호, 정지·삭제된 계정으로 각각 로그인한다. | 모두 401 `AUTH_INVALID_CREDENTIALS`다. 미존재 계정 응답에는 입력 비밀번호가 없다. 기존 계정이 있는 실패 케이스에서 Redis Session 인덱스 수를 보존한다. |
| AU-02-03 | 잘못된 이메일, 사용자명, 빈 `loginId`, 빈 값·공백 비밀번호 또는 JSON `{}`를 보낸다. | 바인딩된 입력의 의미 오류를 401 `AUTH_INVALID_CREDENTIALS`로 반환한다. |
| AU-02-04 | 등록 Session이 있는 계정으로 10회 더 로그인하고 각 토큰으로 프로필을 조회한다. | Redis 인덱스에는 최신 Session 10개만 남고 가장 오래된 Session 본문은 제거된다. 가장 오래된 토큰은 401 `AUTH_TOKEN_INVALID`, 나머지 토큰은 모두 같은 사용자의 프로필을 조회한다. |

## 저장소 장애

[PostgresOutageTests](../../../tests/CloudSharp.Api.IntegrationTests/Failure/PostgresOutageTests.cs) · [RedisOutageTests](../../../tests/CloudSharp.Api.IntegrationTests/Failure/RedisOutageTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 장애-01 | 정상 등록과 프로필 조회 후 PostgreSQL을 중단하고 등록·로그인을 호출한다. | 두 요청 모두 503 `DEPENDENCY_UNAVAILABLE`이며 오류 본문에 요청 비밀번호가 없다. |
| 장애-02 | 정상 등록과 프로필 조회 후 Redis를 중단하고 기존 계정의 로그인과 새 계정 등록을 호출한다. Redis를 재시작한 뒤 새 계정으로 로그인하고 등록을 반복한다. | 장애 중 요청은 503 `DEPENDENCY_UNAVAILABLE`이다. 등록 오류에는 비밀번호나 `accessToken`이 없다. 복구 뒤 새 계정으로 로그인할 수 있고, 저장된 비밀번호 해시를 확인할 수 있다. 재등록은 409 `USER_EMAIL_CONFLICT`이며 두 사용자만 저장된다. |
