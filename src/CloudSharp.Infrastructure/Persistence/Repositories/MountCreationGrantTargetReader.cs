using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountCreationGrantTargetReader(AppDbContext dbContext) : IMountCreationGrantTargetReader
{
    public Task<Result<MountCreationGrantTargets>> ReadAsync(
        long spaceId,
        IReadOnlyCollection<Guid> rolePublicIds,
        IReadOnlyCollection<Guid> userPublicIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rolePublicIds);
        ArgumentNullException.ThrowIfNull(userPublicIds);

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                if (context.Database.CurrentTransaction is null)
                {
                    throw new InvalidOperationException("Mount grant targets must be read in a write transaction.");
                }

                var userIds = userPublicIds.ToArray();
                var users = new Dictionary<Guid, MountCreationUserTarget>();
                if (userIds.Length > 0)
                {
                    // Include LEFT rows so PostgreSQL can recheck a row that changes while we wait for its lock.
                    await context.Database.SqlQuery<long>($"""
                        SELECT membership.id AS "Value"
                        FROM space_memberships AS membership
                        JOIN users AS account ON account.id = membership.user_id
                        WHERE membership.space_id = {spaceId} AND account.public_id = ANY ({userIds})
                        ORDER BY membership.user_id, membership.id
                        FOR SHARE OF membership
                        """).ToArrayAsync(ct);

                    var rows = await (
                        from user in context.Users.AsNoTracking()
                        where userIds.Contains(user.PublicId)
                        from membership in context.SpaceMemberships.AsNoTracking()
                            .Where(member => member.SpaceId == spaceId && member.UserId == user.Id)
                            .OrderBy(member => member.Status == SpaceMembershipStatus.Left)
                            .ThenByDescending(member => member.LeftAt)
                            .ThenByDescending(member => member.Id)
                            .Take(1)
                            .DefaultIfEmpty()
                        from assignment in context.SpaceMembershipRoles.AsNoTracking()
                            .Where(link => membership != null && link.SpaceId == spaceId && link.MembershipId == membership.Id)
                            .DefaultIfEmpty()
                        select new { User = user, Membership = membership, Assignment = assignment })
                        .ToArrayAsync(ct);

                    foreach (var group in rows.GroupBy(row => row.User.PublicId))
                    {
                        var first = group.First();
                        var assignments = group.Select(row => row.Assignment).OfType<Entities.SpaceMembershipRoleEntity>();
                        users.Add(group.Key, new MountCreationUserTarget(first.User.ToDomain(), first.Membership?.ToDomain(assignments)));
                    }
                }

                var roles = await context.SpaceRoles.AsNoTracking()
                    .Where(role => role.SpaceId == spaceId && rolePublicIds.Contains(role.PublicId))
                    .ToArrayAsync(ct);
                return new MountCreationGrantTargets(roles.ToDictionary(role => role.PublicId, role => role.ToDomain()), users);
            },
            cancellationToken);
    }
}
