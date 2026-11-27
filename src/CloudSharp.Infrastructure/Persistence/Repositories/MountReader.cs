using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountReader(AppDbContext dbContext) : IMountReader
{
    public Task<Result<Mount?>> ReadAsync(
        long spaceId,
        Guid mountPublicId,
        CancellationToken cancellationToken = default) =>
        dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var mounts = context.Mounts.AsNoTracking()
                    .Where(mount => mount.SpaceId == spaceId && mount.PublicId == mountPublicId);
                var rows = await MountSnapshotQuery.IncludeGrants(context, mounts).ToArrayAsync(ct);
                return rows.GroupBy(row => row.Mount.Id).Select(MountSnapshotQuery.ToDomain).SingleOrDefault();
            },
            cancellationToken);
}
