# User API

`/api/v2/me`에 등록된 내 계정 API다. 서버는 인증된 Session에서 요청자를 결정한다. [라우트 구현](../../src/CloudSharp.Api/Endpoints/Users/UserEndpoints.cs) · [내 계정 유즈케이스](../use-cases/auth-use-cases.md#2-내-계정) · [User API 통합 테스트 케이스](../tests/api-integration/user-api-integration-test-cases.md)

| ID | API | 메서드·경로 | 요청 | 성공 응답 |
|---|---|---|---|---|
| US-01 | [내 프로필 조회](#us-01-내-프로필-조회) | `GET /api/v2/me` | Session Bearer | 200, 사용자 프로필과 User `ETag` |
| US-03 | [비밀번호 변경](#us-03-비밀번호-변경) | `PUT /api/v2/me/password` | Session Bearer, User `If-Match`, JSON `currentPassword`·`newPassword` | 204, 새 User `ETag` |

## 공통 인증과 응답

- [Auth API](auth-api.md)에서 발급받은 Session을 `Authorization: Bearer {accessToken}`으로 전달한다. 헤더 누락은 401 `AUTH_TOKEN_REQUIRED`, MCP credential은 401 `AUTH_TOKEN_TYPE_NOT_ALLOWED`, 잘못됐거나 만료·폐기된 Session은 401 `AUTH_TOKEN_INVALID`다. 저장소 가용성 장애는 503 `DEPENDENCY_UNAVAILABLE`이다. [Session 인증 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/SessionAuthenticationEndpointTests.cs)
- 성공과 오류 응답에 `Cache-Control: no-store`를 적용한다. 오류 본문의 `requestId`는 응답의 `X-Correlation-ID`와 같다.

## US-01 내 프로필 조회

- 로그인한 사용자의 최신 프로필을 반환한다. JSON 필드는 `userPublicId` (UUIDv7), `email`, `userName`, `displayName`, `systemRole`, `status`, `createdAt`, `updatedAt`이다. `userName`과 `displayName`은 `null`일 수 있다. `systemRole`은 `USER`·`SYSTEM_ADMIN`과 같은 문자열이다. [프로필 응답 DTO](../../src/CloudSharp.Api/Endpoints/Users/Responses/UserProfileResponse.cs)
- `ETag`는 같은 조회의 User 버전으로 생성한다. 비밀번호를 바꿀 때 이 헤더 값을 `If-Match`에 전달한다. [ETag 포맷](../../src/CloudSharp.Api/Contracts/Mapping/ETagFormatter.cs)

## US-03 비밀번호 변경

- `currentPassword`는 최대 128자, `newPassword`는 8~128자다. 두 값은 필수이며 공백만으로 구성할 수 없다. 성공하면 본문 없는 204와 갱신된 `ETag`를 반환한다. 현재 Session은 유지하고 다른 Session을 폐기한다. [비밀번호 변경 요청 DTO](../../src/CloudSharp.Api/Endpoints/Users/Requests/ChangePasswordRequest.cs) · [비밀번호 변경 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Auth/PasswordChangeEndpointTests.cs)
- `If-Match`는 `"v7"`처럼 양수 버전을 담은 강한 ETag 하나다. 누락은 428 `PRECONDITION_REQUIRED`, 잘못된 형식·와일드카드·복수 값은 400 `REQUEST_VALIDATION_FAILED` (`ifMatch` / `INVALID_ETAG`), 저장된 버전과 불일치는 412 `PRECONDITION_FAILED`다. 현재 비밀번호 불일치는 400 `USER_PASSWORD_MISMATCH`다. [If-Match 파서](../../src/CloudSharp.Api/Contracts/Mapping/IfMatchETag.cs)
- 계정 커밋 후 Redis Session 갱신에 실패하면 503 `DEPENDENCY_UNAVAILABLE`을 반환한다. 새 비밀번호는 저장되며 새 비밀번호로 다시 로그인할 수 있다. [커밋 후 장애 통합 테스트](../../tests/CloudSharp.Api.IntegrationTests/Failure/PasswordChangePostCommitFailureTests.cs)
