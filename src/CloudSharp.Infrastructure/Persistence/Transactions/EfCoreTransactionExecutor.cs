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
