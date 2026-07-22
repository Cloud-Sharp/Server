using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Transactions;

internal sealed class EfCoreUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.TrySaveChangesAsync(cancellationToken);
}
