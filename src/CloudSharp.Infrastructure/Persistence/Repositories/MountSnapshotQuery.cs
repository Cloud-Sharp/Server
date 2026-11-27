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
            TargetType = MountOverwriteTargetType.Role,
            grant.AllowMask,
            grant.DenyMask,
        });
        var userGrants = context.MountUserGrants.AsNoTracking().Select(grant => new
        {
            grant.MountId,
            TargetId = grant.UserId,
            TargetType = MountOverwriteTargetType.User,
            grant.AllowMask,
            grant.DenyMask,
        });

        return from mount in mounts
            join grant in roleGrants.Concat(userGrants) on mount.Id equals grant.MountId into grants
            from grant in grants.DefaultIfEmpty()
            select new Row
            {
                Mount = mount,
                TargetId = (long?)grant.TargetId,
                TargetType = (MountOverwriteTargetType?)grant.TargetType,
                AllowMask = (long?)grant.AllowMask,
                DenyMask = (long?)grant.DenyMask,
            };
    }

    public static Mount ToDomain(IGrouping<long, Row> group)
    {
        var mount = group.First().Mount;
        var roleGrants = group.Where(row => row.TargetType == MountOverwriteTargetType.Role)
            .Select(row => new MountRoleGrantEntity
            {
                MountId = mount.Id,
                SpaceId = mount.SpaceId,
                RoleId = row.TargetId!.Value,
                AllowMask = row.AllowMask!.Value,
                DenyMask = row.DenyMask!.Value,
            });
        var userGrants = group.Where(row => row.TargetType == MountOverwriteTargetType.User)
            .Select(row => new MountUserGrantEntity
            {
                MountId = mount.Id,
                UserId = row.TargetId!.Value,
                AllowMask = row.AllowMask!.Value,
                DenyMask = row.DenyMask!.Value,
            });
        return mount.ToDomain(roleGrants, userGrants);
    }

    public sealed class Row
    {
        public required MountEntity Mount { get; init; }
        public long? TargetId { get; init; }
        public MountOverwriteTargetType? TargetType { get; init; }
        public long? AllowMask { get; init; }
        public long? DenyMask { get; init; }
    }
}
