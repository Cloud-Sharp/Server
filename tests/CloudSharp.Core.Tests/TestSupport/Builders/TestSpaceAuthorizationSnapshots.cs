using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

public static class TestSpaceAuthorizationSnapshots
{
    public static readonly Guid SpacePublicId = Guid.Parse("01977b58-32aa-7c60-a17e-0840c6df1200");
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    public static CurrentActor Session(long userId = 42, SystemRole systemRole = SystemRole.User) =>
        CurrentActor.ForSession(userId, Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec001"),
            systemRole, Guid.Parse("01977b58-9ccc-7674-9554-3f85adeea222"));

    public static SpaceAuthorizationQuery Query(
        CurrentActor? actor = null,
        IEnumerable<SpacePermission>? permissions = null,
        IEnumerable<SpaceStatus>? statuses = null,
        Guid? spacePublicId = null,
        bool allowSystemAdminBypass = true) =>
        new(actor ?? Session(), spacePublicId ?? SpacePublicId,
            permissions ?? [SpacePermission.SpaceView], statuses ?? [SpaceStatus.Active, SpaceStatus.Archived],
            allowSystemAdminBypass);

    public static Space Space(
        SpaceStatus status = SpaceStatus.Active,
        long ownerUserId = 21,
        Guid? publicId = null,
        long id = 7,
        string name = "Product",
        string? description = null,
        long? allowedBytes = null,
        long usedBytes = 0,
        long reservedBytes = 0,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var created = createdAt ?? Now.AddDays(-7);
        var updated = updatedAt ?? Now;
        return global::CloudSharp.Core.Domain.Spaces.Space.Reconstitute(
            id, publicId ?? SpacePublicId, ownerUserId, name, description, status,
            allowedBytes, usedBytes, reservedBytes, version, created, updated,
            status == SpaceStatus.Archived ? updated : null,
            status is SpaceStatus.Deleting or SpaceStatus.Deleted ? updated : null,
            status == SpaceStatus.Deleted ? updated : null);
    }

    public static SpaceMembership Membership(
        SpaceMembershipStatus status = SpaceMembershipStatus.Active,
        long userId = 42,
        long spaceId = 7,
        long id = 55,
        params long[] roleIds) =>
        SpaceMembership.Reconstitute(
            id, Guid.CreateVersion7(), spaceId, userId, status, 1, Now.AddDays(-3), Now,
            status == SpaceMembershipStatus.Suspended ? Now : null,
            status == SpaceMembershipStatus.Left ? Now : null,
            (roleIds.Length == 0 ? [101L] : roleIds)
                .Select(roleId => MembershipRoleAssignment.Create(roleId, 21, Now.AddDays(-3))));

    public static SpaceRole Role(
        long id = 101,
        long spaceId = 7,
        IEnumerable<SpacePermission>? permissions = null,
        SpaceRoleSystemKey? systemKey = null,
        DateTimeOffset? deletedAt = null) =>
        SpaceRole.Reconstitute(
            id, Guid.CreateVersion7(), spaceId, "Role", "ROLE", systemKey, null, 1, false,
            1, Now.AddDays(-4), Now, deletedAt,
            permissions ?? [SpacePermission.SpaceView, SpacePermission.FilesRead]);

    public static SpaceAuthorizationSnapshot MemberSnapshot(
        SpaceStatus status = SpaceStatus.Active,
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active,
        IEnumerable<SpacePermission>? permissions = null) =>
        new(Space(status), Membership(membershipStatus), [Role(permissions: permissions)]);
}
