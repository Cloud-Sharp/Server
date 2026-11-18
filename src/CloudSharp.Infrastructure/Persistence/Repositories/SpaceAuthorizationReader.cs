using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceAuthorizationReader(AppDbContext dbContext) : ISpaceAuthorizationReader
{
    public Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
        Guid spacePublicId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var rows = await (
                    from space in context.Spaces.AsNoTracking()
                    where space.PublicId == spacePublicId
                    from membership in context.SpaceMemberships.AsNoTracking()
                        .Where(member =>
                            member.SpaceId == space.Id
                            && member.UserId == userId
                            && (member.Status == SpaceMembershipStatus.Active
                                || member.Status == SpaceMembershipStatus.Suspended))
                        .DefaultIfEmpty()
                    from assignment in context.SpaceMembershipRoles.AsNoTracking()
                        .Where(link =>
                            membership != null
                            && link.MembershipId == membership.Id
                            && link.SpaceId == space.Id)
                        .DefaultIfEmpty()
                    from role in context.SpaceRoles.AsNoTracking()
                        .Where(candidate =>
                            assignment != null
                            && candidate.Id == assignment.RoleId
                            && candidate.SpaceId == space.Id)
                        .DefaultIfEmpty()
                    select new SpaceAuthorizationRow
                    {
                        Space = space,
                        Membership = membership,
                        Assignment = assignment,
                        Role = role,
                    })
                    .ToArrayAsync(ct);

                if (rows.Length == 0)
                {
                    return null;
                }

                var membershipEntity = rows[0].Membership;
                var assignments = rows
                    .Where(row => row.Assignment is not null)
                    .Select(row => row.Assignment!)
                    .ToArray();
                var restoredMembership = membershipEntity?.ToDomain(assignments);
                var roles = rows
                    .Where(row => row.Role is not null)
                    .Select(row => row.Role!.ToDomain())
                    .ToArray();
                return new SpaceAuthorizationSnapshot(rows[0].Space.ToDomain(), restoredMembership, roles);
            },
            cancellationToken);
    }

    private sealed class SpaceAuthorizationRow
    {
        public required SpaceEntity Space { get; init; }
        public SpaceMembershipEntity? Membership { get; init; }
        public SpaceMembershipRoleEntity? Assignment { get; init; }
        public SpaceRoleEntity? Role { get; init; }
    }
}
