using System.Data;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Idempotency;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Transactions;

internal sealed class EfCoreTransactionExecutor(
    AppDbContext dbContext,
    IdempotencyTransactionContext? idempotencyContext = null) : ITransactionExecutor
{
    public async Task<Result> ExecuteAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureNoActiveTransaction();
        idempotencyContext?.GetTransaction(typeof(void));

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async strategyCancellationToken =>
            {
                try
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        isolationLevel,
                        strategyCancellationToken);

                    var result = await operation(strategyCancellationToken);
                    if (result.IsFailed)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        return result;
                    }

                    await transaction.CommitAsync(strategyCancellationToken);
                    return result;
                }
                finally
                {
                    // transaction이 dispose된 뒤 정리해야 다음 시도나 같은 scope의 후속
                    // transaction이 이전 EF 상태를 재사용하지 않는다.
                    dbContext.ChangeTracker.Clear();
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail(CommonError.DependencyUnavailable());
        }
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureNoActiveTransaction();
        var idempotency = idempotencyContext?.GetTransaction(typeof(T));

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async strategyCancellationToken =>
            {
                try
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        isolationLevel,
                        strategyCancellationToken);

                    var result = idempotency is null
                        ? await operation(strategyCancellationToken)
                        : await ExecuteIdempotentOperationAsync(idempotency, operation, strategyCancellationToken);
                    if (result.IsFailed)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        idempotency?.MarkRolledBack();
                        return result;
                    }

                    idempotency?.MarkCommitPending();
                    await transaction.CommitAsync(strategyCancellationToken);
                    idempotency?.MarkCommitted();
                    return result;
                }
                finally
                {
                    dbContext.ChangeTracker.Clear();
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            if (idempotency is not null)
            {
                await idempotency.RecoverAfterCommitFailureAsync(cancellationToken);
            }

            return Result.Fail<T>(CommonError.DependencyUnavailable());
        }
    }

    private static async Task<Result<T>> ExecuteIdempotentOperationAsync<T>(
        IdempotencyTransactionContext.Transaction idempotency,
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        var prepared = await idempotency.PrepareAsync(cancellationToken);
        if (prepared.IsFailed)
        {
            return Result.Fail<T>(prepared.Errors);
        }

        var result = await operation(cancellationToken);
        if (result.IsFailed)
        {
            return result;
        }

        var completion = await idempotency.SaveCompletionAsync(result.Value, cancellationToken);
        return completion.IsSuccess ? result : Result.Fail<T>(completion.Errors);
    }

    private void EnsureNoActiveTransaction()
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Nested database transactions are not supported.");
        }
    }
}
