using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row recording the role assigned to a Space membership.</summary>
public sealed class SpaceMembershipRoleEntity
{
    public long MembershipId { get; set; }
    public long RoleId { get; set; }
    public long SpaceId { get; set; }
    public long AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public SpaceMembershipEntity Membership { get; set; } = null!;

    public MembershipRoleAssignment ToDomain() =>
        MembershipRoleAssignment.Create(RoleId, AssignedByUserId, AssignedAt);

    public static SpaceMembershipRoleEntity FromDomain(
        SpaceMembershipEntity membership,
        MembershipRoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(assignment);

        return new SpaceMembershipRoleEntity
        {
            MembershipId = membership.Id,
            RoleId = assignment.RoleId,
            SpaceId = membership.SpaceId,
            AssignedByUserId = assignment.AssignedByUserId,
            AssignedAt = assignment.AssignedAt,
            Membership = membership,
        };
    }
}
