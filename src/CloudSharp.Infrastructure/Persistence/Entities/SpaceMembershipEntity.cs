using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Space membership. Role assignments are stored separately.</summary>
public sealed class SpaceMembershipEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long SpaceId { get; set; }
    public long UserId { get; set; }
    public SpaceMembershipStatus Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }

    /// <summary>Restores the membership and its materialized role assignments.</summary>
    public SpaceMembership ToDomain(IEnumerable<SpaceMembershipRoleEntity> roleAssignments)
    {
        ArgumentNullException.ThrowIfNull(roleAssignments);

        return SpaceMembership.Reconstitute(
            id: Id,
            publicId: PublicId,
            spaceId: SpaceId,
            userId: UserId,
            status: Status,
            version: Version,
            joinedAt: JoinedAt,
            updatedAt: UpdatedAt,
            suspendedAt: SuspendedAt,
            leftAt: LeftAt,
            roleAssignments: roleAssignments.Select(assignment => assignment.ToDomain()));
    }

    public static SpaceMembershipEntity FromDomain(SpaceMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var entity = new SpaceMembershipEntity { Id = membership.Id };
        entity.UpdateFrom(membership);
        return entity;
    }

    public void UpdateFrom(SpaceMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        PublicId = membership.PublicId;
        SpaceId = membership.SpaceId;
        UserId = membership.UserId;
        Status = membership.Status;
        Version = membership.Version;
        JoinedAt = membership.JoinedAt;
        UpdatedAt = membership.UpdatedAt;
        SuspendedAt = membership.SuspendedAt;
        LeftAt = membership.LeftAt;
    }
}
