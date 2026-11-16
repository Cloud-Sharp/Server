using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

public static class TestSpaceInvites
{
    public const string PlainToken = "cs_inv_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public const string TokenHashValue = "BCDEFGHIJKLMNOPQRSTUVWXYZabcdefgh_123456789";

    public static readonly DateTimeOffset Now = new(2026, 11, 16, 3, 0, 0, TimeSpan.Zero);

    public static TokenHash Hash() => TokenHash.Create(TokenHashValue).Value;

    public static Guid PublicId(int sequence) =>
        Guid.Parse($"01920000-0000-7000-8000-{sequence:000000000000}");

    public static SpaceInvite Stored(
        long id = 100,
        Guid? publicId = null,
        long spaceId = 7,
        long createdByUserId = 42,
        SpaceInviteStatus status = SpaceInviteStatus.Active,
        int useCount = 0,
        int? maxUses = 2,
        DateTimeOffset? expiresAt = null,
        long version = 3,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? revokedAt = null)
    {
        var created = createdAt ?? Now.AddDays(-7);
        var updated = updatedAt ?? Now.AddDays(-1);
        return SpaceInvite.Reconstitute(
            id,
            publicId ?? PublicId(100),
            spaceId,
            createdByUserId,
            Hash(),
            status,
            useCount,
            maxUses,
            expiresAt ?? Now.AddDays(7),
            version,
            created,
            updated,
            revokedAt ?? (status == SpaceInviteStatus.Revoked ? updated : null));
    }

    public static SpaceInvite Copy(SpaceInvite invite) =>
        SpaceInvite.Reconstitute(
            invite.Id,
            invite.PublicId,
            invite.SpaceId,
            invite.CreatedByUserId,
            invite.TokenHash,
            invite.Status,
            invite.UseCount,
            invite.MaxUses,
            invite.ExpiresAt,
            invite.Version,
            invite.CreatedAt,
            invite.UpdatedAt,
            invite.RevokedAt);

    public static SpaceMembership Copy(SpaceMembership membership) =>
        SpaceMembership.Reconstitute(
            membership.Id,
            membership.PublicId,
            membership.SpaceId,
            membership.UserId,
            membership.Status,
            membership.Version,
            membership.JoinedAt,
            membership.UpdatedAt,
            membership.SuspendedAt,
            membership.LeftAt,
            membership.RoleAssignments);

    public static SpaceRole Role(
        long id = 101,
        long spaceId = 7,
        SpaceRoleSystemKey systemKey = SpaceRoleSystemKey.Viewer,
        string name = "Viewer",
        DateTimeOffset? deletedAt = null) =>
        SpaceRole.Reconstitute(
            id,
            PublicId((int)id),
            spaceId,
            name,
            name.ToUpperInvariant(),
            systemKey,
            null,
            0,
            true,
            1,
            Now.AddDays(-10),
            deletedAt ?? Now.AddDays(-9),
            deletedAt,
            []);
}
