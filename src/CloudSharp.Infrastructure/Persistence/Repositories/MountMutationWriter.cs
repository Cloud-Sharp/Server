using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountMutationWriter(AppDbContext dbContext) : IMountMutationWriter
{
    public async Task<Result> UpdateAsync(
        Mount mount,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mount);
        cancellationToken.ThrowIfCancellationRequested();

        if (mount.Id <= 0)
        {
            throw new InvalidOperationException("UpdateAsync requires a mount with a generated id.");
        }

        var roleGrantResult = await dbContext.ExecuteReadAsync(
            (context, ct) => context.MountRoleGrants.AsNoTracking()
                .Where(grant => grant.MountId == mount.Id && grant.SpaceId == mount.SpaceId)
                .ToArrayAsync(ct),
            cancellationToken);
        if (roleGrantResult.IsFailed)
        {
            return Result.Fail(roleGrantResult.Errors);
        }

        var userGrantResult = await dbContext.ExecuteReadAsync(
            (context, ct) => context.MountUserGrants.AsNoTracking()
                .Where(grant => grant.MountId == mount.Id)
                .ToArrayAsync(ct),
            cancellationToken);
        if (userGrantResult.IsFailed)
        {
            return Result.Fail(userGrantResult.Errors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entity = dbContext.Mounts.Local.FirstOrDefault(tracked => tracked.Id == mount.Id);
        if (entity is null)
        {
            entity = MountEntity.FromDomain(mount);
            dbContext.Mounts.Update(entity);
        }
        else
        {
            entity.UpdateFrom(mount);
        }

        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        ReconcileRoleGrants(dbContext, entity, roleGrantResult.Value, mount);
        ReconcileUserGrants(dbContext, entity, userGrantResult.Value, mount);
        return Result.Ok();
    }

    private static void ReconcileRoleGrants(
        AppDbContext dbContext,
        MountEntity mountEntity,
        MountRoleGrantEntity[] persistedGrants,
        Mount mount)
    {
        var grantsByRoleId = persistedGrants.ToDictionary(grant => grant.RoleId);
        foreach (var entry in dbContext.ChangeTracker.Entries<MountRoleGrantEntity>())
        {
            var grant = entry.Entity;
            if (grant.MountId == mount.Id && grant.SpaceId == mount.SpaceId)
            {
                grantsByRoleId[grant.RoleId] = grant;
            }
        }

        var remainingGrants = mount.Grants
            .Where(grant => grant.TargetType == MountGrantTargetType.Role)
            .ToDictionary(grant => grant.TargetId);
        foreach (var grant in grantsByRoleId.Values)
        {
            if (!remainingGrants.Remove(grant.RoleId, out var desired))
            {
                dbContext.MountRoleGrants.Remove(grant);
                continue;
            }

            var entry = dbContext.Entry(grant);
            if (entry.State != EntityState.Added)
            {
                entry.State = EntityState.Unchanged;
            }

            grant.UpdateFrom(desired);
        }

        foreach (var grant in remainingGrants.Values)
        {
            dbContext.MountRoleGrants.Add(MountRoleGrantEntity.FromDomain(mountEntity, grant));
        }
    }

    private static void ReconcileUserGrants(
        AppDbContext dbContext,
        MountEntity mountEntity,
        MountUserGrantEntity[] persistedGrants,
        Mount mount)
    {
        var grantsByUserId = persistedGrants.ToDictionary(grant => grant.UserId);
        foreach (var entry in dbContext.ChangeTracker.Entries<MountUserGrantEntity>())
        {
            var grant = entry.Entity;
            if (grant.MountId == mount.Id)
            {
                grantsByUserId[grant.UserId] = grant;
            }
        }

        var remainingGrants = mount.Grants
            .Where(grant => grant.TargetType == MountGrantTargetType.User)
            .ToDictionary(grant => grant.TargetId);
        foreach (var grant in grantsByUserId.Values)
        {
            if (!remainingGrants.Remove(grant.UserId, out var desired))
            {
                dbContext.MountUserGrants.Remove(grant);
                continue;
            }

            var entry = dbContext.Entry(grant);
            if (entry.State != EntityState.Added)
            {
                entry.State = EntityState.Unchanged;
            }

            grant.UpdateFrom(desired);
        }

        foreach (var grant in remainingGrants.Values)
        {
            dbContext.MountUserGrants.Add(MountUserGrantEntity.FromDomain(mountEntity, grant));
        }
    }
}
