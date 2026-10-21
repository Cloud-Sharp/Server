---
name: cloudsharp-repository
description: >
  CloudSharp Core 영속성 port의 Infrastructure EF Core repository 구현을
  작성하거나 리팩토링할 때 사용한다. 공용 DbContext ExecuteReadAsync 조회,
  독립적인 도메인 snapshot, 변경 등록, 추적 엔티티 재사용, optimistic concurrency,
  UnitOfWork/TransactionExecutor 책임 경계를 다룬다.
  엔티티의 필드별 도메인 매핑만 변경하는 요청은 cloudsharp-persistence-entity 대상이다.
---

# CloudSharp Repository 작성

## 계약과 기준 코드 확인

먼저 `.llm/index.md`, 대상 `.llm/domains/<aggregate>.md`의 Core 계약·동시성·트랜잭션 항목, 관련 `.llm/database/<area>.md`를 읽는다. 실제 Core port와 도메인, EF 엔티티·Configuration, repository, DI 등록과 관련 테스트를 대조한다. User의 기준은 다음과 같다.

- Core port: `src/CloudSharp.Core/Abstractions/Persistence/IUserRepository.cs`
- 구현: `src/CloudSharp.Infrastructure/Persistence/Repositories/UserRepository.cs`
- 공용 조회: `src/CloudSharp.Infrastructure/Persistence/DbContext/DbContextReadExtensions.cs`
- 오류 분류: 같은 디렉터리의 `PostgresAvailabilityErrors.cs`
- 저장·트랜잭션: `src/CloudSharp.Infrastructure/Persistence/Transactions/`

해당 aggregate 계약을 우선한다. User에 필요한 특정 조회나 등록 후 재조회 절차를 다른 aggregate에 무조건 추가하지 않는다.

## 책임 배치

| 위치 | 책임 |
|---|---|
| Core port | 도메인 타입·Result·CancellationToken으로 영속성 계약 표현. EF/Infrastructure 타입 비노출 |
| Infrastructure repository | 쿼리, 조회 결과 없음 처리, Add/Update 변경 등록, EF 추적과 원래 concurrency 값 설정 |
| 공용 `DbContextReadExtensions` | 조회 callback 실행과 알려진 PostgreSQL 가용성 장애의 Result 변환 |
| 영속성 엔티티 | `ToDomain` / `FromDomain` / `UpdateFrom` 데이터 매핑 |
| EF Configuration | 테이블·컬럼·인덱스·제약·concurrency token 등 스키마 매핑 |
| UnitOfWork | `SaveChangesAsync`, 저장 시 constraint·동시성·가용성 오류의 기존 계약에 따른 변환 |
| TransactionExecutor | 트랜잭션 시작·commit/rollback, 실행 전략, 각 시도 종료 시 추적 상태 정리 |

구현체는 기존 패턴에 맞춰 `internal sealed`로 두고 scoped `AppDbContext`를 주입받는다. repository에 마지막으로 읽은 객체, 이후 저장할 대상, 호출 순서를 기억하는 필드를 두지 않는다. scope의 EF ChangeTracker는 지정한 엔티티 변경을 추적하는 데 사용한다. 비즈니스 행위·인가·여러 aggregate 조율은 도메인/UseCase에 둔다.

## 조회는 공용 DbContext 확장 사용

repository별 private `ExecuteReadAsync`나 동일한 try/catch를 복제하지 않는다. 공용 확장의 계약은 다음과 같다.

```csharp
public static async Task<Result<TValue>> ExecuteReadAsync<TContext, TValue>(
    this TContext dbContext,
    Func<TContext, CancellationToken, Task<TValue>> read,
    CancellationToken cancellationToken = default)
    where TContext : Microsoft.EntityFrameworkCore.DbContext
```

callback은 전달받은 `context`로 실제 쿼리를 실행한다. 바깥 `dbContext`만 캡처하여 확장의 receiver를 사용하지 않는 형태를 만들지 않는다.

```csharp
return dbContext.ExecuteReadAsync(
    async (context, ct) =>
    {
        var entity = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == id, ct);
        return entity?.ToDomain();
    },
    cancellationToken);
```

- Core 계약이 독립적인 도메인 snapshot을 요구하면 `AsNoTracking`으로 조회하고 materialization 뒤 `ToDomain()`을 호출한다. EF가 번역할 수 없는 도메인 변환을 SQL 필터나 projection에 넣지 않는다.
- 결과가 없으면 port 계약에 따라 성공 값 `null`을 반환한다. Exists 조회는 `AnyAsync`의 bool을 반환한다. repository가 임의로 NotFound 실패로 바꾸거나 조회한 도메인 변경을 자동 저장하지 않는다.
- null 인수·ID 범위 등의 처리는 해당 port의 계약을 유지한다. 예를 들어 User의 없는 ID 조회와 생성된 Id가 없는 User의 Update는 같은 입력 정책이 아니다.
- 공용 확장은 null `dbContext`와 null callback을 방어하고 이미 취소된 token을 확인한다. 같은 token을 callback에 전달하며, callback은 `ct`를 EF 비동기 API까지 전달한다. 같은 scoped context에서 쿼리를 병렬 실행하지 않는다.
- 성공한 callback의 값은 `Result.Ok`로 감싼다. callback 안에서 이미 Result로 감싸 이중 Result를 만들지 않는다. 저장이나 새로운 retry·transaction 정책은 공용 조회 확장에 추가하지 않는다.

### 조회 오류와 취소

공용 확장은 `PostgresAvailabilityErrors.IsDependencyUnavailable(exception)` 예외 필터로 알려진 연결·서버 가용성 장애만 골라 `CommonError.DependencyUnavailable()`로 변환한다. 모든 `NpgsqlException`, 모든 timeout, 모든 `Exception`을 가용성 오류로 취급하지 않는다.

취소(`OperationCanceledException`, 내부 원인이 취소인 경우 포함), SQL·무결성·인증 오류, 잘못된 저장값의 도메인 복원 오류와 프로그래밍 예외는 기존 예외로 전파한다. 실패를 `null`/`false` 같은 정상 결과로 숨기지 않는다. 분류가 필요하면 기존 공용 분류기를 재사용하고 repository마다 판정식을 추가하지 않는다.

## Add와 Update는 변경만 등록

- Add는 `Entity.FromDomain(domain)`으로 별도 영속성 인스턴스를 만들어 등록한다. 입력 도메인을 추적하거나 DB가 생성한 PK를 입력 객체에 다시 쓰지 않는다. `FromDomain`은 신규 기본 Id와 기존 Id를 그대로 복사한다.
- User 등록은 UseCase가 UnitOfWork로 저장한 뒤 같은 트랜잭션 안에서 PublicId로 재조회하여 생성된 내부 Id를 얻는다. Add가 저장·commit까지 수행하거나 반환 계약을 임의로 바꾸지 않는다.
- Update는 port가 요구하는 유효한 영속 PK를 확인한다. scoped context에 같은 Id의 엔티티가 있으면 그 인스턴스에 `UpdateFrom(domain)`을 호출한다. 없으면 `FromDomain`으로 만든 엔티티를 Update 등록한다. 같은 PK의 인스턴스를 중복 추적하지 않는다.
- `UpdateFrom`은 수신 엔티티 Id를 보존하고 선택값의 null도 반영한다. 필드별 복원·복사 목록은 엔티티가 소유한다. repository에서 Core setter를 열거나 `User.Reconstitute(...)` 매핑을 다시 작성하지 않는다.
- Add/Update 성공은 변경 등록 성공이다. 실제 저장 성공을 뜻하지 않는다. repository에서 `SaveChanges`, commit/rollback, `ChangeTracker.Clear()`를 호출하지 않는다.

## Optimistic concurrency

`expectedVersion`은 UseCase가 **도메인 변경 전에 조회한 버전**이다. 변경된 `domain.Version`에서 원래 값을 역산하거나 repository가 마지막 조회 버전을 기억하지 않는다.

repository는 등록할 엔티티의 `Version` property entry에 `OriginalValue = expectedVersion`을 설정한다. 현재 Version은 도메인이 결정한 값을 유지하며 매핑이나 repository가 증가시키지 않는다. User 계약처럼 변경이 없는 Update도 경쟁 검사가 필요하면 `Version.IsModified = true`로 UPDATE 실행을 보장한다. 중간 조회로 버전 조건을 대체하지 않는다.

충돌 검사는 UnitOfWork의 저장 시점에 발생하며 기존 `PRECONDITION_FAILED` 변환을 따른다. uniqueness 충돌도 저장 경로에서 처리한다. 사전 Exists 확인만으로 DB 고유 제약을 대신하지 않는다. 성공·실패·예외 후 다음 시도에 이전 변경이 남지 않게 하는 추적 정리는 TransactionExecutor의 책임이다.

## 변경과 검증 범위

기존 공용 확장과 계약을 재사용한다. 단순 중복 제거를 위해 generic base repository, 새 인터페이스, 조회용 별도 context 수명이나 추가 abstraction을 일괄 도입하지 않는다. 요청과 실제 재사용 필요성이 있다면 기존 의존·트랜잭션 경계를 유지하는 범위에서 판단한다.

- 기존 테스트가 영향받는 계약을 검증하면 재사용한다. 단순한 코드 이동만을 확인하는 테스트를 추가하지 않는다. 계약 변경이나 기존 검증에서 드러나지 않는 회귀 가능성이 있을 때 필요한 테스트를 보강한다.
- 공용 조회 계약을 변경할 때는 성공 값(bool/null/도메인), context와 token 전달, 취소와 예외 분류 등 영향받는 동작을 확인한다. DB 없는 callback 테스트로 검증할 수 있는 오류 처리를 위해 실제 DB를 요구하지 않는다.
- repository 변경: 독립 snapshot, 명시한 대상만 staging, 저장 전 미반영, 입력 도메인 Id 보존, 같은 PK 추적 재사용, 변경 없는 Update의 버전 검사와 stale version 충돌 등 영향받는 계약을 관련 테스트로 확인한다.
- 실제 SQL·EF 저장·트랜잭션 동작은 PostgreSQL 통합 테스트로 확인한다. 관련 build/test 결과를 보고하고 Docker 등 환경 때문에 실행하지 못한 검증은 통과로 처리하지 않는다.

이 스킬은 `.agents/skills/cloudsharp-repository/SKILL.md`와 `.claude/skills/cloudsharp-repository/SKILL.md`에 같은 내용으로 유지한다.
