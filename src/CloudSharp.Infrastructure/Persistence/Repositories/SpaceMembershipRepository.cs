using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceMembershipRepository(AppDbContext dbContext) : ISpaceMembershipRepository
{
    public Task<Result<SpaceMembership?>> FindByPublicIdAsync(
        long spaceId,
        Guid membershipPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            (context, ct) => ReadSnapshotAsync(
                context,
                context.SpaceMemberships.AsNoTracking().Where(candidate =>
                    candidate.SpaceId == spaceId && candidate.PublicId == membershipPublicId),
                ct),
            cancellationToken);
    }

    public Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
        long spaceId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            (context, ct) => ReadSnapshotAsync(
                context,
                context.SpaceMemberships.AsNoTracking()
                    .Where(candidate => candidate.SpaceId == spaceId
                        && candidate.UserId == userId
                        && (candidate.Status == SpaceMembershipStatus.Active
                            || candidate.Status == SpaceMembershipStatus.Suspended
                            || candidate.Status == SpaceMembershipStatus.Left))
                    .OrderBy(candidate => candidate.Status == SpaceMembershipStatus.Left)
                    .ThenByDescending(candidate => candidate.LeftAt)
                    .ThenByDescending(candidate => candidate.Id),
                ct),
            cancellationToken);
    }

    public Task<Result> AddAsync(
        SpaceMembership membership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        cancellationToken.ThrowIfCancellationRequested();

        StageAdd(dbContext, membership);
        return Task.FromResult(Result.Ok());
    }

    public async Task<Result> UpdateAsync(
        SpaceMembership membership,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        cancellationToken.ThrowIfCancellationRequested();

        if (membership.Id <= 0)
        {
            throw new InvalidOperationException("UpdateAsync requires a membership with a generated id.");
        }

        var linkResult = await dbContext.ExecuteReadAsync(
            (context, ct) => context.SpaceMembershipRoles.AsNoTracking()
                .Where(link => link.SpaceId == membership.SpaceId && link.MembershipId == membership.Id)
                .ToArrayAsync(ct),
            cancellationToken);
        if (linkResult.IsFailed)
        {
            return Result.Fail(linkResult.Errors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entity = dbContext.SpaceMemberships.Local.FirstOrDefault(tracked => tracked.Id == membership.Id);
        if (entity is null)
        {
            entity = SpaceMembershipEntity.FromDomain(membership);
            dbContext.SpaceMemberships.Update(entity);
        }
        else
        {
            entity.UpdateFrom(membership);
        }

        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        ReconcileLinks(dbContext, entity, linkResult.Value, membership);
        return Result.Ok();
    }

    /// <summary>Membership과 Role 연결을 함께 등록한다. 저장 시 EF가 생성된 Membership Id를 연결에 전파한다.</summary>
    internal static void StageAdd(AppDbContext dbContext, SpaceMembership membership)
    {
        var entity = SpaceMembershipEntity.FromDomain(membership);
        dbContext.SpaceMemberships.Add(entity);
        foreach (var assignment in membership.RoleAssignments)
        {
            dbContext.SpaceMembershipRoles.Add(SpaceMembershipRoleEntity.FromDomain(entity, assignment));
        }
    }

    internal static async Task<SpaceMembership?> ReadSnapshotAsync(
        AppDbContext context,
        IQueryable<SpaceMembershipEntity> candidates,
        CancellationToken ct)
    {
        var rows = await (
            from member in candidates.Take(1)
            from link in context.SpaceMembershipRoles.AsNoTracking()
                .Where(assignment => assignment.SpaceId == member.SpaceId && assignment.MembershipId == member.Id)
                .DefaultIfEmpty()
            select new { Membership = member, Link = link })
            .ToArrayAsync(ct);

        return rows.Length == 0
            ? null
            : rows[0].Membership.ToDomain(rows.Where(row => row.Link is not null).Select(row => row.Link!));
    }

    private static void ReconcileLinks(
        AppDbContext dbContext,
        SpaceMembershipEntity entity,
        SpaceMembershipRoleEntity[] links,
        SpaceMembership membership)
    {
        var linksByRoleId = links.ToDictionary(link => link.RoleId);
        // 저장 전 추가하거나 삭제한 연결도 다음 Update에 반영한다.
        foreach (var entry in dbContext.ChangeTracker.Entries<SpaceMembershipRoleEntity>())
        {
            var link = entry.Entity;
            if (link.SpaceId == membership.SpaceId && link.MembershipId == membership.Id)
            {
                linksByRoleId[link.RoleId] = link;
            }
        }

        var remainingAssignments = membership.RoleAssignments.ToDictionary(assignment => assignment.RoleId);
        foreach (var link in linksByRoleId.Values)
        {
            if (!remainingAssignments.Remove(link.RoleId, out var assignment))
            {
                dbContext.SpaceMembershipRoles.Remove(link);
                continue;
            }

            var entry = dbContext.Entry(link);
            if (entry.State == EntityState.Deleted)
            {
                // 삭제를 취소하면 같은 키의 연결을 다시 추적할 필요가 없다.
                entry.State = EntityState.Unchanged;
            }

            if (link.AssignedByUserId == assignment.AssignedByUserId && link.AssignedAt == assignment.AssignedAt)
            {
                continue;
            }

            if (entry.State == EntityState.Detached)
            {
                entry.State = EntityState.Unchanged;
            }

            link.AssignedByUserId = assignment.AssignedByUserId;
            link.AssignedAt = assignment.AssignedAt;
        }

        foreach (var assignment in remainingAssignments.Values)
        {
            dbContext.SpaceMembershipRoles.Add(SpaceMembershipRoleEntity.FromDomain(entity, assignment));
        }
    }
}
