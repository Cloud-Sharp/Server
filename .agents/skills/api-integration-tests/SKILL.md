---
name: api-integration-tests
description: >
  CloudSharp API의 Docker 기반 통합 테스트를 작성·수정·실행할 때 사용한다.
  Testcontainers PostgreSQL·Redis와 WebApplicationFactory로 실제 HTTP 파이프라인,
  인증, 오류 계약과 저장소 반영을 검증한다. 순수 유닛 테스트나 repository만의
  Infrastructure 테스트, 배포용 Docker 구성 작성은 대상이 아니다.
---

# Docker 기반 API 통합 테스트

## 대상과 기준 확인

테스트는 `tests/CloudSharp.Api.IntegrationTests`에 작성한다. PostgreSQL·Redis는 Docker에서 실행하고, API는 실제 `Program`을 `WebApplicationFactory<Program>`으로 부팅해 `HttpClient`로 호출한다. API 이미지 자체의 container 실행은 배포 검증을 요청받았을 때 별도 범위로 다룬다.

먼저 저장소의 현재 코드와 다음 기준을 확인한다. 문서와 구현이 다르면 차이를 드러내며, 테스트를 통과시키려고 목표 계약이나 제품 동작을 임의로 바꾸지 않는다.

- `.llm/index.md`, `.llm/api/common-contract.md`, 대상 `.llm/api/<area>.md`, `.llm/api/validation.md`.
- 인증 관련 작업은 `.llm/auth/authentication.md`, `.llm/auth/session-token.md`, `.llm/auth/security-testing.md`와 [Auth 시나리오](references/auth-scenarios.md).
- `src/CloudSharp.Api/Program.cs`, API DI·HTTP 구성, 실제 대상 endpoint와 use case.
- 테스트 프로젝트·`Directory.Packages.props`, 기존 PostgreSQL·Redis fixture, migration 파일의 실제 존재 여부.

기존 TestHost·container fixture·reset helper를 재사용한다. README만 있거나 helper가 없으면 필요한 공통 기반부터 작성한다. `IntegrationTestBase`, `RequestFactories`, `ExtractErrorCodes`, Respawn이 이미 있다고 가정하지 않는다. 새 패키지가 필요하면 중앙 패키지 관리에 맞추며 관계없는 업그레이드는 하지 않는다.

## 실제 호스트와 컨테이너

- NUnit과 기존 `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Microsoft.AspNetCore.Mvc.Testing`을 사용한다. 설치된 패키지 버전의 API를 따른다.
- 실제 PostgreSQL provider, Redis adapter, password/token 처리, 인증·인가, `AddCloudSharpHttp()`의 binding·validation·오류 middleware를 유지한다. SQLite·EF InMemory·fake repository·fake Session store·가짜 인증 scheme으로 대체한 결과를 Docker API 통합 테스트라고 하지 않는다.
- 컨테이너에는 기존 테스트와 호환되는 명시적 이미지 버전, 임의 host port, 실행별 전용 DB·자원을 사용한다. `localhost:5432/6379`, 고정 container 이름, 개발·운영 연결 문자열에 의존하지 않는다.
- 공통 async fixture가 컨테이너를 시작하고 readiness를 확인한 다음 factory를 생성한다. 컨테이너 시작을 매 테스트마다 반복하지 않으며, 고정 sleep 대신 readiness와 제한 시간 있는 polling을 사용한다.
- 시작된 컨테이너의 `GetConnectionString()`을 `ConnectionStrings:PostgreSql`, `ConnectionStrings:Redis`에 넣고, 실행 중 생성한 32바이트 이상 임의 키를 Base64로 `TokenHashing:ActiveKey`에 주입한다. `AddCloudSharpApplication()`의 등록 시점 검증보다 먼저 보이도록 factory의 host 설정을 구성한다. 늦은 `ConfigureTestServices`만으로 해결된다고 가정하지 않는다.
- factory 설정에는 `UseSetting` 또는 적절한 구성 provider를 사용한다. appsettings·user secrets·프로세스 전역 환경변수는 바꾸지 않는다. 환경은 명시적으로 `Production`을 기본으로 하여 개발 환경의 오류 처리 차이가 결함을 가리지 않게 한다.
- `Program` 접근성은 빌드·생성 코드로 확인한다. Web SDK가 만든 public entry point가 있으면 중복 선언하지 않는다. 접근이 실제로 막힐 때만 최소한의 friend assembly 또는 entry point 변경을 한다.
- 실제 `AddPostgreSqlPersistence()` 등록을 재사용한다. 임의의 `UseNpgsql()` 교체로 PostgreSQL enum 매핑·translator·트랜잭션·동시성 구성을 잃지 않는다.
- client마다 HTTPS base address, `AllowAutoRedirect=false`, `HandleCookies=false`를 명시한다. 보호 요청은 가입·로그인 API에서 얻은 실제 Session token을 `Authorization: Bearer`로 전달한다.

## 스키마·상태 격리·정리

- migration이 있으면 테스트 fixture에서 적용한다. 아직 migration이 없으면 기존 패턴처럼 **격리된 테스트 DB에만** `EnsureCreatedAsync()`를 사용할 수 있으며 migration 검증은 미완료라고 보고한다. 같은 DB에 `EnsureCreated`와 `Migrate`를 섞거나 API 시작에 자동 migration을 추가하지 않는다.
- 공용 setup에서 각 테스트 전 PostgreSQL과 Redis 상태를 함께 초기화한다. 대상은 이 실행이 만든 자원으로 제한한다. DB reset은 migration history·필수 기준 데이터를 보존하고, Redis reset은 전용 DB만 비우거나 소유 key만 지운다. Respawn은 이미 있을 때 활용하며 도입을 강제하지 않는다.
- 공유 자원을 reset하는 fixture는 `[NonParallelizable]`로 직렬화한다. 병렬화가 필요하면 DB·schema·Redis namespace나 container를 fixture별로 완전히 분리한다. 한 fixture의 고유 사용자명만으로 공용 reset 충돌이 해결되지는 않는다.
- schema 초기화·seed·관찰은 테스트 helper의 별도 DI scope에서 실행하고 dispose한다. HTTP request의 transaction을 외부 테스트 transaction으로 감싸 rollback만 하는 방식은 격리가 아니다. 전용 테스트 DB의 명시적 `TRUNCATE`·Redis flush도 소유 범위를 확인한 helper에서만 수행한다.
- 초기화가 중간 실패해도 생성된 factory·connection·container를 정리한다. host와 client를 먼저 종료하고 Redis connection·container를 역순으로 dispose한다. Resource Reaper는 유지하며 재사용은 기본으로 활성화하지 않는다. 시스템 전체 prune, 다른 container 중지·삭제는 하지 않는다.

## 테스트 작성과 검증

- 기능 폴더의 `*EndpointTests.cs`에 `[TestFixture]`, `[Category("ApiIntegration")]`를 적용한다. 이름은 `Action_ShouldExpectedResult`, 조건은 `[TestCase]`·`[TestCaseSource]`로 구분하고 성공·실패 테스트는 분리한다.
- 신규 assertion은 NUnit `Assert.That`을 사용한다. 정상 입력 builder와 seeded Bogus가 필요하면 공통화하되 경계값은 명시한다. 테스트끼리 계정·token·순서를 공유하지 않는다.
- HTTP 요청으로 status, content type, 계약 필드, header를 확인한다. 내부 handler나 endpoint delegate 직접 호출은 유닛 테스트이며 이 단계의 대체가 아니다.
- 실패는 `error.code`, `error.details[].field/code`, `requestId`와 correlation header를 검사한다. JSON parsing·알 수 없는 필드·자동 DataAnnotations 검증·인증 실패처럼 handler 이전 경로도 포함한다. 기본 RFC ProblemDetails가 반환된다고 가정하지 않는다.
- 성공은 camelCase, 문자열 enum, PublicId, ETag·Location·캐시 header 등 대상 계약을 확인한다. DB 반영·Redis 세션·rollback·unique constraint·stale version 거부처럼 통합 경계가 중요한 side effect는 새 scope/connection으로 확인한다. 테스트에서 생성한 request DTO와 동일 타입으로 응답을 읽어 wire 계약 오류를 가리지 않는다.
- token·password·Authorization·전체 token hash·secret이 들어간 응답을 assertion message, snapshot, test log에 출력하지 않는다. 실패 진단은 status·오류 코드·correlation ID 등으로 제한하고, secret 유출 검사는 실제 값을 출력하지 않는 boolean assertion으로 한다.
- clock이 필요한 경우 `IClock`만 통제할 수 있다. Redis TTL과 Lua의 `TIME`은 실제 서버 시간이므로 과거 고정 clock으로 정상 세션을 발급하지 않는다. 필요하면 Redis 서버 시각을 기준으로 clock을 초기화하고 각 테스트에서 복구한다. 만료는 clock 전진 또는 테스트 소유 payload/TTL 조작으로 검증하며 임의 sleep에 의존하지 않는다.
- 장애 테스트는 별도 fixture·factory·전용 container를 사용한다. API 시작과 정상 요청을 먼저 확인하고 해당 테스트가 소유한 dependency만 중단·차단한 뒤, bounded timeout 안에 표준 503과 fail-closed를 검증한다. 가짜 오류 반환만으로 실제 Docker 장애 검증을 대신하지 않는다. 남은 테스트에 연결 풀·중단 자원을 공유하지 않는다.

## 실행과 완료 보고

저장소 루트에서 실행한다. 아래 명령은 테스트를 **작성하는 작업**에서 사용하며, 스킬 파일만 작성·수정해 달라는 요청이면 컨테이너나 테스트를 자동 실행하지 않는다.

```powershell
docker info
dotnet build cloudsharp.sln
dotnet test tests/CloudSharp.Api.IntegrationTests/CloudSharp.Api.IntegrationTests.csproj --filter "TestCategory=ApiIntegration"
```

개발 중에는 위 filter에 `FullyQualifiedName` 조건을 추가해 대상 fixture부터 확인한다. 완료 전 선택된 API integration suite를 실행하고 테스트가 실제로 발견되었는지 확인한다. category 누락으로 0개가 실행된 결과는 통과가 아니다.

Docker 미실행·이미지 pull 실패·readiness timeout은 명확한 실행 실패/환경 제한으로 남긴다. `Assert.Ignore`, `Assert.Inconclusive`, catch 후 성공 반환, mock fallback으로 통과 처리하지 않는다. 기능 검증 전에 인프라가 실패했다면 제품 결함과 구분해 보고한다.

최종 보고에는 구현·검증한 HTTP 시나리오, 사용한 이미지와 스키마 초기화 방식, 실제 실행 명령·통과/실패/건너뜀 수, 실행하지 못한 범위와 정리 결과를 적는다. 목표 문서의 후속 기능을 현 구현의 완료 조건으로 몰래 확대하지 않는다.

공식 참고: [Testcontainers + ASP.NET Core](https://dotnet.testcontainers.org/examples/aspnet/), [Resource Reaper](https://dotnet.testcontainers.org/api/resource_reaper/), [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).

이 스킬과 참조 파일은 `.agents/skills/api-integration-tests/`와 `.claude/skills/api-integration-tests/`에 같은 내용으로 유지한다. 본문의 저장소 경로는 저장소 루트 기준이다.
