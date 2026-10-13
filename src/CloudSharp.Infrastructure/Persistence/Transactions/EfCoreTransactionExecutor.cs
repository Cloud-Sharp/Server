using System.Data;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Transactions;

internal sealed class EfCoreTransactionExecutor(AppDbContext dbContext) : ITransactionExecutor
{
    public Task<Result> ExecuteAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureNoActiveTransaction();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async strategyCancellationToken =>
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

    public Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureNoActiveTransaction();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async strategyCancellationToken =>
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
                dbContext.ChangeTracker.Clear();
            }
        }, cancellationToken);
    }

    private void EnsureNoActiveTransaction()
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Nested database transactions are not supported.");
        }
    }
}
