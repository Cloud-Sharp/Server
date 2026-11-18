using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceDetailsReader(AppDbContext dbContext) : ISpaceDetailsReader
{
    public Task<Result<SpaceDetailsSnapshot?>> ReadAsync(
        long spaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var row = await (
                    from space in context.Spaces.AsNoTracking()
                    join owner in context.Users.AsNoTracking() on space.OwnerUserId equals owner.Id
                    where space.Id == spaceId
                    select new { Space = space, OwnerUserPublicId = owner.PublicId })
                    .SingleOrDefaultAsync(ct);

                return row is null
                    ? null
                    : new SpaceDetailsSnapshot(row.Space.ToDomain(), row.OwnerUserPublicId);
            },
            cancellationToken);
    }
}
