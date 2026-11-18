using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceRoleReader(AppDbContext dbContext) : ISpaceRoleReader
{
    public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
        long spaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var rows = await context.SpaceRoles.AsNoTracking()
                    .Where(role => role.SpaceId == spaceId)
                    .ToArrayAsync(ct);
                IReadOnlyList<SpaceRole> roles = rows.Select(row => row.ToDomain()).ToArray();
                return roles;
            },
            cancellationToken);
    }
}
