# Persistence Transactions

Core의 transaction 포트를 EF Core로 구현하고 Dapper 명령을 같은 PostgreSQL
transaction에 참여시키는 규칙을 정의한다.

## 구성 요소

| 구현 | 수명 | 역할 |
|---|---|---|
| `EfCoreTransactionExecutor` | Scoped | execution strategy 안에서 transaction을 시작하고 결과에 따라 commit/rollback한다. |
| `EfCoreUnitOfWork` | Scoped | `AppDbContext.TrySaveChangesAsync`를 호출한다. |
| `DapperDbSession` | Scoped | `AppDbContext`의 connection과 현재 transaction으로 `CommandDefinition`을 만든다. |

세 구성 요소와 모든 EF Core 리포지토리는 동일한 요청 scope의 `AppDbContext`를
공유해야 한다. `AddPostgreSqlPersistence`가 필요한 scoped 등록을 수행한다.

## Dapper 리포지토리 구현

Dapper 리포지토리는 별도의 `NpgsqlConnection`을 생성하지 않는다. `DapperDbSession`이
제공하는 connection과 `CreateCommand`를 함께 사용해야 현재 EF Core transaction에
자동으로 참여한다.

```csharp
internal sealed class SpaceQuotaRepository(DapperDbSession dbSession)
{
    public async Task<bool> TryReserveAsync(
        long spaceId,
        long requestedBytes,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE spaces
            SET storage_reserved_bytes = storage_reserved_bytes + @RequestedBytes
            WHERE id = @SpaceId
              AND storage_used_bytes + storage_reserved_bytes + @RequestedBytes
                  <= storage_quota_bytes
            """;

        var command = dbSession.CreateCommand(
            sql,
            new { SpaceId = spaceId, RequestedBytes = requestedBytes },
            cancellationToken: cancellationToken);

        var affectedRows = await dbSession.Connection.ExecuteAsync(command);
        return affectedRows == 1;
    }
}
```

`DapperDbSession.Connection`은 `AppDbContext`가 소유한다. 리포지토리는 connection을
직접 dispose하거나 닫지 않는다. `CreateCommand`를 사용하지 않고 SQL 문자열만 Dapper에
넘기면 현재 transaction이 빠질 수 있으므로 금지한다.

## 혼합 트랜잭션 실행 순서

```csharp
return await transactionExecutor.ExecuteAsync(
    async transactionCancellationToken =>
    {
        // Dapper 명령은 즉시 실행되지만 아직 commit되지 않는다.
        var updated = await quotaRepository.TryReserveAsync(
            spaceId,
            requestedBytes,
            transactionCancellationToken);
        if (!updated)
        {
            return Result.Fail("Quota exceeded.");
        }

        // EF Core 리포지토리는 변경을 추적한다.
        await uploadRepository.AddAsync(upload, transactionCancellationToken);

        // 추적 변경을 DB에 보내고, 성공 Result가 반환되면 executor가 commit한다.
        var saveResult = await unitOfWork.SaveChangesAsync(
            transactionCancellationToken);
        return saveResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(saveResult.Errors);
    },
    cancellationToken: cancellationToken);
```

콜백이 실패 Result를 반환하거나 예외·취소로 종료되면 Dapper 명령과 EF Core 저장을
모두 rollback한다. `SaveChangesAsync` 성공은 transaction commit을 의미하지 않는다.

## 조회 기준

JOIN, CTE, window function을 사용하는 복잡한 SQL이라도 단일 `SELECT` 하나라면 보통
명시적 transaction이 필요하지 않다. 다음 경우에만 transaction executor를 사용한다.

- 여러 조회가 동일한 스냅샷을 봐야 하는 경우
- `SELECT ... FOR UPDATE` 등 row lock이 필요한 경우
- 조회 결과를 기준으로 Dapper 또는 EF Core 변경을 수행하는 경우
- 여러 변경과 Outbox append를 함께 확정해야 하는 경우

불필요하게 긴 조회 transaction은 snapshot과 lock을 오래 유지할 수 있으므로 피한다.

## Execution strategy와 외부 I/O

수동 transaction 전체는 `AppDbContext.Database.CreateExecutionStrategy()`가 실행한다.
재시도 전략이 활성화되면 콜백 전체가 다시 실행될 수 있으므로 콜백에는 동일 DB의
재실행 가능한 작업만 둔다.

Redis, 파일시스템, object storage, 외부 API, 메시지 브로커 직접 발행은 콜백에 넣지
않는다. DB 변경과 후속 메시지 전달을 결합해야 하면 Outbox를 같은 transaction에서
저장한다.

중첩 transaction, ambient `TransactionScope`, 분산 transaction은 지원하지 않는다.
