using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceMembershipDto(
    Guid MembershipPublicId,
    SpaceMembershipUserDto User,
    SpaceMembershipStatus Status,
    IReadOnlyList<SpaceMembershipRoleDto> Roles,
    DateTimeOffset JoinedAt,
    DateTimeOffset UpdatedAt)
{
    internal static SpaceMembershipDto FromSnapshot(SpaceMembershipReadSnapshot snapshot)
    {
        var membership = snapshot.Membership;
        var assignedRoleIds = membership.RoleAssignments.Select(assignment => assignment.RoleId).ToHashSet();
        var roles = snapshot.Roles
            .Where(role => role.SpaceId == membership.SpaceId
                && role.SystemKey.HasValue
                && assignedRoleIds.Contains(role.Id))
            .Select(role => new SpaceMembershipRoleDto(role.PublicId, role.SystemKey!.Value, role.Name))
            .ToArray();

        return new SpaceMembershipDto(
            membership.PublicId,
            new SpaceMembershipUserDto(snapshot.UserPublicId, snapshot.UserName, snapshot.DisplayName),
            membership.Status,
            roles,
            membership.JoinedAt,
            membership.UpdatedAt);
    }
}
