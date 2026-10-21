# Auth API Docker Integration Tests

현재 구현된 Auth·내 계정 API 4개를 실제 PostgreSQL, Redis, ASP.NET Core HTTP pipeline으로 검증한다.
이 문서는 2026-10-21 실행 결과와 테스트 기반 구성을 기록한다.

## 범위

| API | 검증 |
|---|---|
| `POST /api/v2/auth/registrations` | 201·Location·응답 계약, DB·password hash·Redis Session, 중복 409, 동시 중복의 단일 생성 |
| `POST /api/v2/auth/sessions` | 정상 로그인·이메일 정규화, 계정 없음·정지·삭제·잘못된 비밀번호·의미상 잘못된 입력의 동일 401 |
| `GET /api/v2/me` | 실제 Session 인증, ETag, 최신 User 상태·SecurityVersion·SystemRole, 만료·손상·폐기 거부, 최대 10 Session |
| `PUT /api/v2/me/password` | 204·증가한 ETag, 새 비밀번호 적용, 현재 Session 유지·다른 Session 폐기, 428·400·412, 실패 시 상태 보존 |

추가 검증:

- malformed JSON·unknown field·잘못된 자료형·자동 DataAnnotations 검증·415 응답
- 표준 오류 envelope, `requestId`와 correlation header 일치, `Cache-Control: no-store`
- camelCase, 문자열 enum, `UserPublicId` UUIDv7, 내부 식별자·password hash·token hash 비노출
- PostgreSQL 중단 시 503 fail closed
- Redis 중단 시 503 fail closed, Redis 장애 중 가입 계정 commit 보존과 로그인 복구
- 비밀번호 변경 commit 직후 Redis 중단: 모든 기존 Session 거부, 새 비밀번호 로그인 성공, read transaction 비간섭

로그아웃, 프로필 PATCH, 멱등성, rate limit, 활동 갱신, MCP는 현재 구현 범위가 아니므로 테스트하지 않는다.

## 테스트 기반

- 이미지: `postgres:17-alpine`, `redis:7-alpine`
- API host: `WebApplicationFactory<Program>`, 환경 `Production`
- 실제 `AddCloudSharpApplication()` DI를 유지한다. PostgreSQL enum mapping, 실제 Redis adapter,
  PBKDF2 password hasher, HMAC token hasher, Session 인증 middleware를 대체하지 않는다.
- 연결 문자열과 실행별 임의 32바이트 HMAC key를 host 구성에 주입한다. appsettings·user secrets·
  프로세스 전역 환경변수는 변경하지 않는다.
- HTTP client는 HTTPS base address, `AllowAutoRedirect=false`, `HandleCookies=false`를 사용한다.
- `IClock`은 Redis `TIME`으로 초기화한 mutable clock으로 교체한다. 각 테스트 전에 다시 Redis
  서버 시각으로 초기화해 idle·absolute 만료 경계를 실제 저장소 시각과 분리해 검증한다.
- migration이 아직 없으므로 전용 테스트 DB에만 `EnsureCreatedAsync()`를 사용한다.
  migration 적용 검증은 이 suite의 완료 범위가 아니다.
- 일반 테스트는 하나의 PostgreSQL·Redis container와 host를 공유한다. 각 테스트 전에
  `TRUNCATE TABLE users RESTART IDENTITY CASCADE`와 `FLUSHDB`를 실행하고 `[NonParallelizable]`로
  직렬화한다. 장애 테스트는 전용 container·host를 사용하며 Redis 재시작 검증은 고정 host port를 쓴다.
- 비밀번호 변경 후처리 장애는 테스트 전용 EF interceptor가 write transaction commit 직후 Redis를
  중단하도록 구성한다. read transaction commit에서는 중단하지 않는다.
- 테스트는 독립적인 wire request/response record와 NUnit `Assert.That`을 사용한다. DB 관찰은
  host DI의 새 scope에서 수행한다. token·password·hash·전체 응답은 실패 메시지에 기록하지 않는다.

## 실행

저장소 루트에서 실행한다.

```powershell
docker info
dotnet build cloudsharp.sln
dotnet test tests/CloudSharp.Api.IntegrationTests/CloudSharp.Api.IntegrationTests.csproj --filter "TestCategory=ApiIntegration"
```

전체 회귀:

```powershell
dotnet test cloudsharp.sln
```

## 2026-10-21 실행 결과

- Docker: `docker info` 성공 (Docker 29.3.1)
- Build: `dotnet build cloudsharp.sln` 성공, 경고 0·오류 0
- ApiIntegration: 76개 발견, 76 통과, 0 실패, 0 건너뜀 (약 15~20초)
- 전체 solution: 945개 통과, 0 실패, 0 건너뜀
  - Core 303, Api 128, Architecture 15, Infrastructure 423, Api.Integration 76
  - Worker IntegrationTests는 현재 테스트가 없어 0개다.
- 테스트 종료 후 전용 PostgreSQL·Redis container와 host는 dispose됐다. Testcontainers Resource
  Reaper는 유지하며 시스템 전체 Docker prune이나 다른 container 중지·삭제는 수행하지 않는다.

## 통합 테스트에서 확인·수정한 사항

- Routing의 `AcceptsMatcherPolicy`가 JSON endpoint에 잘못된 Content-Type을 보낸 요청을 endpoint
  middleware 이전에 빈 415로 거부하고 있었다. `UnsupportedMediaTypeStatusCodeHandler`를
  `UseStatusCodePages`에 연결해 표준 `REQUEST_VALIDATION_FAILED` 오류 envelope과
  `Cache-Control: no-store`를 반환하도록 최소 수정했다.
- 기존 `RedisSessionStoreIntegrationTests`의 corrupt payload 테스트가 구현·문서와 달리 예외를
  기대하고 있었다. 현재 계약(손상 Session은 세션 없음으로 처리하고 본문 정리)에 맞게 기대값을
  수정했고 `RedisSessionStore` 요약 주석도 실제 동작과 일치시켰다.
