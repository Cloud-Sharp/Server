using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceOwnershipMembershipRepository(AppDbContext dbContext)
    : ISpaceOwnershipMembershipRepository
{
    public Task<Result<SpaceMembership?>> FindByUserIdAsync(
        long spaceId,
        long userId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            (context, token) => SpaceMembershipRepository.ReadSnapshotAsync(
                context,
                context.SpaceMemberships.AsNoTracking()
                    .Where(candidate => candidate.SpaceId == spaceId
                        && candidate.UserId == userId
                        && (candidate.Status == SpaceMembershipStatus.Active
                            || candidate.Status == SpaceMembershipStatus.Suspended)),
                token),
            ct);
    }

    public Task<Result<SpaceRole?>> FindManagedAdminRoleAsync(
        long spaceId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, token) => (await context.SpaceRoles.AsNoTracking()
                .Where(role => role.SpaceId == spaceId
                    && role.IsManaged
                    && role.SystemKey == SpaceRoleSystemKey.Admin
                    && role.DeletedAt == null)
                .OrderBy(role => role.Id)
                .FirstOrDefaultAsync(token))?.ToDomain(),
            ct);
    }

    public Task<Result> DeleteAsync(
        SpaceMembership membership,
        long expectedVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ct.ThrowIfCancellationRequested();

        if (membership.Id <= 0)
        {
            throw new InvalidOperationException("DeleteAsync requires a membership with a generated id.");
        }

        var entity = dbContext.SpaceMemberships.Local.FirstOrDefault(tracked => tracked.Id == membership.Id)
            ?? SpaceMembershipEntity.FromDomain(membership);
        dbContext.SpaceMemberships.Remove(entity);

        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;

        return Task.FromResult(Result.Ok());
    }

    public Task<Result> AddAsync(
        SpaceMembership membership,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ct.ThrowIfCancellationRequested();

        SpaceMembershipRepository.StageAdd(dbContext, membership);
        return Task.FromResult(Result.Ok());
    }
}
