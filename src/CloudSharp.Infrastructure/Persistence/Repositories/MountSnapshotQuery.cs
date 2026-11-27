using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal static class MountSnapshotQuery
{
    public static IQueryable<Row> IncludeGrants(AppDbContext context, IQueryable<MountEntity> mounts)
    {
        var roleGrants = context.MountRoleGrants.AsNoTracking().Select(grant => new
        {
            grant.MountId,
            TargetId = grant.RoleId,
            TargetType = MountGrantTargetType.Role,
            grant.PermissionMask,
        });
        var userGrants = context.MountUserGrants.AsNoTracking().Select(grant => new
        {
            grant.MountId,
            TargetId = grant.UserId,
            TargetType = MountGrantTargetType.User,
            grant.PermissionMask,
        });

        return from mount in mounts
            join grant in roleGrants.Concat(userGrants) on mount.Id equals grant.MountId into grants
            from grant in grants.DefaultIfEmpty()
            select new Row
            {
                Mount = mount,
                TargetId = (long?)grant.TargetId,
                TargetType = (MountGrantTargetType?)grant.TargetType,
                PermissionMask = (long?)grant.PermissionMask,
            };
    }

    public static Mount ToDomain(IGrouping<long, Row> group)
    {
        var mount = group.First().Mount;
        var roleGrants = group.Where(row => row.TargetType == MountGrantTargetType.Role)
            .Select(row => new MountRoleGrantEntity
            {
                MountId = mount.Id,
                SpaceId = mount.SpaceId,
                RoleId = row.TargetId!.Value,
                PermissionMask = row.PermissionMask!.Value,
            });
        var userGrants = group.Where(row => row.TargetType == MountGrantTargetType.User)
            .Select(row => new MountUserGrantEntity
            {
                MountId = mount.Id,
                UserId = row.TargetId!.Value,
                PermissionMask = row.PermissionMask!.Value,
            });
        return mount.ToDomain(roleGrants, userGrants);
    }

    public sealed class Row
    {
        public required MountEntity Mount { get; init; }
        public long? TargetId { get; init; }
        public MountGrantTargetType? TargetType { get; init; }
        public long? PermissionMask { get; init; }
    }
}
