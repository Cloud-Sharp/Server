using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

public static class TestMountReadSnapshots
{
    public const long SpaceId = 7;
    public const long SpaceOwnerUserId = 21;
    public const long ActorUserId = 42;
    public static readonly Guid SpacePublicId = PublicId(SpaceId);
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 4, 30, 0, TimeSpan.Zero);

    public static CurrentActor Actor(long userId = ActorUserId) =>
        CurrentActor.ForSession(userId, PublicId(40_000 + userId), SystemRole.User, PublicId(900_000 + userId));

    public static SpaceAuthorizationSnapshot Owner(
        CurrentActor actor,
        SpaceStatus status = SpaceStatus.Active,
        long spaceId = SpaceId) =>
        new(Space(status, actor.UserId, spaceId), null, []);

    public static SpaceAuthorizationSnapshot Member(
        CurrentActor actor,
        IEnumerable<SpaceRole>? roles = null,
        IEnumerable<long>? assignedRoleIds = null,
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active,
        SpaceStatus spaceStatus = SpaceStatus.Active,
        long spaceId = SpaceId,
        long ownerUserId = SpaceOwnerUserId)
    {
        var assignedRoles = (roles ?? [Role(10_000 + actor.UserId, spaceId)]).ToArray();
        return new SpaceAuthorizationSnapshot(
            Space(spaceStatus, ownerUserId, spaceId),
            Membership(
                actor.UserId,
                spaceId,
                membershipStatus,
                roleIds: assignedRoleIds ?? assignedRoles.Select(role => role.Id)),
            assignedRoles);
    }

    public static Space Space(
        SpaceStatus status = SpaceStatus.Active,
        long ownerUserId = SpaceOwnerUserId,
        long id = SpaceId) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId,
            PublicId(id),
            id,
            name: $"Space {id}",
            createdAt: Now.AddDays(-30),
            updatedAt: Now);

    /// <summary>기본 Role 권한은 Space 조회, 파일 조회와 MOUNT_VIEW다.</summary>
    public static SpaceRole Role(
        long id,
        long spaceId = SpaceId,
        SpaceRoleSystemKey? systemKey = null,
        DateTimeOffset? deletedAt = null,
        IEnumerable<SpacePermission>? permissions = null) =>
        SpaceRole.Reconstitute(
            id,
            PublicId(100_000 + id),
            spaceId,
            $"Role {id}",
            $"ROLE {id}",
            systemKey,
            null,
            1,
            systemKey.HasValue,
            1,
            Now.AddDays(-20),
            Now,
            deletedAt,
            permissions ?? [SpacePermission.SpaceView, SpacePermission.FilesRead, SpacePermission.MountView]);

    public static SpaceMembership Membership(
        long userId,
        long spaceId = SpaceId,
        SpaceMembershipStatus status = SpaceMembershipStatus.Active,
        long? id = null,
        IEnumerable<long>? roleIds = null)
    {
        var membershipId = id ?? 50_000 + userId;
        var assignedRoleIds = (roleIds ?? [10_000 + userId]).ToArray();
        return SpaceMembership.Reconstitute(
            membershipId,
            PublicId(200_000 + membershipId),
            spaceId,
            userId,
            status,
            1,
            Now.AddDays(-10),
            Now,
            status == SpaceMembershipStatus.Suspended ? Now : null,
            status == SpaceMembershipStatus.Left ? Now : null,
            assignedRoleIds.Select(roleId => MembershipRoleAssignment.Create(
                roleId,
                SpaceOwnerUserId,
                Now.AddDays(-5))));
    }

    public static User User(
        long id,
        UserStatus status = UserStatus.Active) =>
        global::CloudSharp.Core.Domain.Users.User.Reconstitute(
            id,
            PublicId(40_000 + id),
            EmailAddress.Create($"user{id}@example.com").Value,
            NormalizedName.Create($"user{id}").Value,
            $"User {id}",
            "hashed-password",
            SystemRole.User,
            status,
            1,
            1,
            Now.AddDays(-60),
            Now,
            status == UserStatus.Suspended ? Now : null,
            status == UserStatus.Deleted ? Now : null);

    public static Mount Mount(
        long id,
        long spaceId = SpaceId,
        MountVisibility visibility = MountVisibility.Public,
        MountStatus status = MountStatus.Active,
        long version = 3,
        IEnumerable<MountPermissionOverwrite>? overwrites = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var created = createdAt ?? Now.AddDays(-10);
        var updated = updatedAt ?? Now;
        return global::CloudSharp.Core.Domain.Mounts.Mount.Reconstitute(
            id,
            PublicId(300_000 + id),
            spaceId,
            $"Mount {id}",
            visibility,
            status,
            version,
            created,
            updated,
            status == MountStatus.Archived ? updated : null,
            overwrites ?? []);
    }

    public static MountPermissionOverwrite Overwrite(
        MountOverwriteTargetType targetType,
        long targetId,
        SpacePermission[]? allow = null,
        SpacePermission[]? deny = null) =>
        MountPermissionOverwrite.Create(targetType, targetId, allow ?? [], deny ?? []).Value;

    /// <summary>endpoint 필터를 통과한 Space context를 만든다. 권한을 생략하면 모든 Space 권한을 갖는다.</summary>
    public static AuthorizedSpaceContext SpaceContext(
        CurrentActor actor,
        IEnumerable<SpacePermission>? permissions = null,
        long spaceId = SpaceId,
        SpaceAuthorizationSource source = SpaceAuthorizationSource.Membership) =>
        new(
            spaceId,
            PublicId(spaceId),
            actor.ActorType,
            actor.UserId,
            source == SpaceAuthorizationSource.Membership ? 50_000 + actor.UserId : null,
            permissions ?? Enum.GetValues<SpacePermission>(),
            source);

    /// <summary>
    /// endpoint 필터를 통과한 Mount context를 만든다. 권한을 생략하면 Mount 범위 권한을 모두 갖는다.
    /// </summary>
    public static AuthorizedMountContext MountContext(
        CurrentActor actor,
        Mount mount,
        IEnumerable<SpacePermission>? permissions = null,
        long? spaceId = null,
        SpaceStatus spaceStatus = SpaceStatus.Active) =>
        new(
            SpaceContext(actor, spaceId: spaceId ?? mount.SpaceId),
            spaceStatus,
            mount.Id,
            mount.PublicId,
            permissions ?? MountScopedPermissions.All);

    public static MountPreviewUserSnapshot PreviewUser(
        long userId,
        IEnumerable<SpaceRole>? roles = null,
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active,
        UserStatus userStatus = UserStatus.Active,
        bool includeMembership = true)
    {
        var assignedRoles = (roles ?? [Role(10_000 + userId)]).ToArray();
        return new MountPreviewUserSnapshot(
            User(userId, userStatus),
            includeMembership
                ? Membership(userId, status: membershipStatus, roleIds: assignedRoles.Select(role => role.Id))
                : null,
            assignedRoles);
    }

    public static IError ReadError(string errorCode) =>
        new Error("The read dependency returned an error.")
            .WithMetadata(CloudSharpError.ErrorCodeMetadataKey, errorCode);

    public static Guid PublicId(long value) =>
        Guid.Parse($"00000000-0000-7000-8000-{value:D12}");
}
