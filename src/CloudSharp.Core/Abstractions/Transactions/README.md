# Transactions

유즈케이스가 데이터베이스 트랜잭션의 업무 경계를 선언하기 위한 Core 포트다.
Core는 EF Core, Npgsql, Dapper를 참조하지 않으며 실제 트랜잭션은 Infrastructure가 실행한다.

## 역할

| 포트 | 역할 |
|---|---|
| `ITransactionExecutor` | 콜백 실행 전 트랜잭션을 시작하고 결과에 따라 commit 또는 rollback한다. |
| `IUnitOfWork` | EF Core가 추적한 변경을 `SaveChangesAsync`로 저장한다. |

`Commit`과 `SaveChanges`는 별도 책임이다. EF Core 리포지토리로 변경한 유즈케이스는
트랜잭션 콜백 안에서 `IUnitOfWork.SaveChangesAsync`를 명시적으로 호출해야 한다.
Dapper 변경 명령은 호출 즉시 실행되지만 트랜잭션 commit 전에는 확정되지 않는다.

## EF Core 리포지토리 사용

```csharp
public sealed class RenameFileUseCase(
    ITransactionExecutor transactionExecutor,
    IUnitOfWork unitOfWork,
    IFileRepository fileRepository)
{
    public Task<Result> ExecuteAsync(
        RenameFileCommand command,
        CancellationToken cancellationToken) =>
        transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var file = await fileRepository.GetAsync(
                    command.FileId,
                    transactionCancellationToken);

                var expectedVersion = file.Version;
                var renameResult = file.Rename(command.Name);
                if (renameResult.IsFailed)
                {
                    return renameResult;
                }

                var updateResult = await fileRepository.UpdateAsync(
                    file,
                    expectedVersion,
                    transactionCancellationToken);
                if (updateResult.IsFailed)
                {
                    return updateResult;
                }

                var saveResult = await unitOfWork.SaveChangesAsync(
                    transactionCancellationToken);
                return saveResult.IsSuccess
                    ? Result.Ok()
                    : Result.Fail(saveResult.Errors);
            },
            cancellationToken: cancellationToken);
}
```

리포지토리는 독립적인 도메인 snapshot을 조회하거나 지정된 변경을 등록할 뿐 저장·commit하지 않는다.
유즈케이스가 관련 리포지토리 호출을 모두 마친 뒤 한 번 저장한다.

Core 도메인과 EF entity는 별개 모델이다. 도메인 객체만 수정한 뒤 UnitOfWork를 호출하면
그 변경은 등록되지 않으므로 `UpdateAsync`로 변경 내용과 수정 전 버전을 명시적으로 전달한다.
신규 User는 `AddAsync` → UnitOfWork 저장 → 같은 트랜잭션의 `FindByPublicIdAsync` 조회로
DB가 생성한 내부 Id를 얻는다.

## Dapper와 EF Core 혼합

Dapper 사용 여부는 Infrastructure 리포지토리 내부에 숨긴다. 유즈케이스는 두 종류의
리포지토리를 구분하지 않고 동일한 트랜잭션 콜백 안에서 호출한다.

```csharp
return await transactionExecutor.ExecuteAsync(
    async transactionCancellationToken =>
    {
        // Infrastructure에서 Dapper 조건부 UPDATE로 구현될 수 있다.
        var reserved = await quotaRepository.TryReserveAsync(
            command.SpaceId,
            command.Size,
            transactionCancellationToken);
        if (!reserved)
        {
            return Result.Fail("Quota reservation failed.");
        }

        // EF Core 변경 추적을 사용하는 리포지토리다.
        await uploadRepository.AddAsync(upload, transactionCancellationToken);

        var saveResult = await unitOfWork.SaveChangesAsync(
            transactionCancellationToken);
        return saveResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(saveResult.Errors);
    },
    cancellationToken: cancellationToken);
```

Infrastructure의 Dapper 리포지토리는 현재 `AppDbContext`의 connection과 transaction을
사용하므로 Dapper 변경과 EF Core `SaveChangesAsync`가 함께 commit 또는 rollback된다.

## 실패와 취소

| 콜백 결과 | 동작 |
|---|---|
| 성공 `Result` | transaction commit 후 결과 반환 |
| 실패 `Result` | transaction rollback 후 실패 결과 반환 |
| 예외 | transaction rollback 후 예외 전파 |
| 취소 | transaction rollback 후 `OperationCanceledException` 전파 |

트랜잭션 콜백 안에서 다른 `ITransactionExecutor.ExecuteAsync`를 호출하는 중첩
트랜잭션은 지원하지 않는다. 여러 작업이 같은 원자성 경계를 가져야 한다면 바깥
유즈케이스의 콜백 안에서 각각의 작업을 직접 호출한다.

변경은 트랜잭션 콜백 안에서 등록한다. Infrastructure executor는 각 실행 전략 시도의
트랜잭션 종료·dispose 후 EF 추적 상태를 정리한다. 성공·실패·예외·취소 이후 다음
트랜잭션이나 재시도로 이전 변경이 넘어가지 않으며, 콜백 진행 중에는 추적을 유지한다.

UnitOfWork는 알려진 unique·concurrency·가용성 실패를 Core 오류로 변환한다.
`SaveChangesAsync`의 성공은 commit을 의미하지 않는다. 취소와 예상 밖 예외는 전파한다.

## 격리 수준

기본 격리 수준은 `IsolationLevel.ReadCommitted`다. 동일 트랜잭션 안의 반복 조회가
같은 스냅샷을 봐야 할 때만 더 강한 수준을 명시한다.

```csharp
return await transactionExecutor.ExecuteAsync(
    operation,
    IsolationLevel.RepeatableRead,
    cancellationToken);
```

격리 수준을 높이는 대신 조건부 `UPDATE`, unique constraint, row lock과 같은 더 작은
동시성 제어 수단으로 해결할 수 있는지 먼저 검토한다.

## 트랜잭션에 포함하지 않는 작업

단일 조회는 여러 statement 간 일관성이나 row lock이 필요하지 않다면 명시적
트랜잭션 없이 실행한다. 다음 작업은 DB transaction과 원자적으로 묶을 수 없고
실행 시간이 길어지므로 콜백 밖에 둔다.

- 파일 또는 오브젝트 스토리지 이동
- Redis 호출
- 외부 HTTP API 호출
- 메시지 브로커 직접 발행

후속 처리가 필요한 이벤트는 업무 변경과 같은 DB transaction에서 Outbox에 기록하고
commit 후 별도 worker가 전달한다.
