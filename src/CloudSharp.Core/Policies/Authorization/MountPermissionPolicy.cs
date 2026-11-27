using System.Collections.Frozen;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// Discord 채널 권한 덮어쓰기와 같은 방식으로 한 사용자의 Mount 유효 권한을 계산하는 순수 정책이다.
/// <list type="number">
/// <item>Space 소유자·활성 ADMIN은 덮어쓰기와 무관하게 Mount 범위 권한을 모두 갖는다.</item>
/// <item>그 외 활성 멤버는 Space Role 권한 합집합 중 Mount 범위 권한을 기본값으로 갖는다.</item>
/// <item><see cref="MountVisibility.Private"/>이면 기본값에서 <see cref="SpacePermission.MountView"/>를 뺀다.</item>
/// <item>현재 Role들의 덮어쓰기를 함께 적용한다. 거부 합집합을 먼저 빼고 허용 합집합을 더한다.</item>
/// <item>사용자 덮어쓰기를 마지막에 적용한다. 거부를 빼고 허용을 더한다.</item>
/// <item>암묵적 권한: 최종 <see cref="SpacePermission.MountView"/>가 없으면 모두 비우고,
/// <see cref="SpacePermission.FilesRead"/>가 없으면 다운로드·쓰기를 뺀다.</item>
/// </list>
/// Space·Mount 상태는 권한에서 빼지 않는다. endpoint가 허용 상태를 선언하고 <see cref="MountAuthorizationPolicy"/>가
/// 요구 권한보다 먼저 검사한다. 응답에 표시할 권한은 <see cref="ExercisablePermissions"/>로 상태를 반영한다.
/// </summary>
public static class MountPermissionPolicy
{
    public static MountPermissionEvaluation Evaluate(
        SpaceAuthorizationSnapshot snapshot,
        long userId,
        Mount mount,
        MountVisibility? visibility = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Evaluate(snapshot.Space, userId, snapshot.Membership, snapshot.Roles, mount, visibility);
    }

    /// <summary>미리보기용 <c>visibility</c>를 생략하면 Mount의 현재 공개 범위를 사용한다.</summary>
    public static MountPermissionEvaluation Evaluate(
        Space space,
        long userId,
        SpaceMembership? membership,
        IReadOnlyCollection<SpaceRole> roles,
        Mount mount,
        MountVisibility? visibility = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(mount);

        if (userId <= 0 || space.Id <= 0 || mount.SpaceId != space.Id || !HasSpaceAccess(space, userId, membership))
        {
            return MountPermissionEvaluation.None;
        }

        var currentRoles = GetCurrentRoles(space, userId, membership, roles);
        var sources = new List<MountPermissionSource>();
        HashSet<SpacePermission> permissions;
        if (space.OwnerUserId == userId || currentRoles.Any(role => role.SystemKey == SpaceRoleSystemKey.Admin))
        {
            permissions = MountScopedPermissions.All.ToHashSet();
            sources.Add(new MountPermissionSource(
                MountPermissionSourceType.Administrator, MountScopedPermissions.All, Empty, null));
        }
        else
        {
            permissions = SpacePermissionPolicy.Evaluate(space, userId, membership, roles)
                .Where(MountScopedPermissions.Contains)
                .ToHashSet();
            AddSource(sources, MountPermissionSourceType.Base, permissions, Empty, null);

            if ((visibility ?? mount.Visibility) == MountVisibility.Private
                && permissions.Remove(SpacePermission.MountView))
            {
                AddSource(sources, MountPermissionSourceType.Visibility, Empty, [SpacePermission.MountView], null);
            }

            var roleAllow = new HashSet<SpacePermission>();
            var roleDeny = new HashSet<SpacePermission>();
            foreach (var role in currentRoles)
            {
                if (mount.FindOverwrite(MountOverwriteTargetType.Role, role.Id) is { } overwrite)
                {
                    roleAllow.UnionWith(overwrite.Allow);
                    roleDeny.UnionWith(overwrite.Deny);
                    AddSource(sources, MountPermissionSourceType.Role, overwrite.Allow, overwrite.Deny, role.PublicId);
                }
            }

            permissions.ExceptWith(roleDeny);
            permissions.UnionWith(roleAllow);

            if (mount.FindOverwrite(MountOverwriteTargetType.User, userId) is { } userOverwrite)
            {
                permissions.ExceptWith(userOverwrite.Deny);
                permissions.UnionWith(userOverwrite.Allow);
                AddSource(sources, MountPermissionSourceType.User, userOverwrite.Allow, userOverwrite.Deny, null);
            }
        }

        ApplyImplicitPermissions(permissions);
        return new MountPermissionEvaluation(permissions.ToFrozenSet(), sources);
    }

    /// <summary>
    /// 유효 권한 중 현재 Space·Mount 상태에서 행사할 수 있는 권한이다. 콘텐츠 변경은 Space와 Mount가 모두
    /// ACTIVE일 때만 가능하므로 그 밖에는 <see cref="SpacePermission.FilesWrite"/>를 뺀다. 인가에는 쓰지 않고
    /// 응답의 myAccess와 미리보기 표시에 사용한다.
    /// </summary>
    public static IReadOnlySet<SpacePermission> ExercisablePermissions(
        IReadOnlySet<SpacePermission> permissions,
        SpaceStatus spaceStatus,
        MountStatus mountStatus)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return spaceStatus == SpaceStatus.Active && mountStatus == MountStatus.Active
            ? permissions.ToFrozenSet()
            : permissions.Where(permission => permission != SpacePermission.FilesWrite).ToFrozenSet();
    }

    /// <summary>
    /// 덮어쓰기를 <paramref name="next"/>로 바꿀 때 달라지는 권한을 행위자가 모두 가졌는지 검사한다.
    /// 자신이 갖지 않은 권한은 다른 대상에게 허용하거나 거부할 수 없다.
    /// </summary>
    public static bool CanChangeOverwrite(
        IReadOnlySet<SpacePermission> actorPermissions,
        MountPermissionOverwrite? current,
        MountPermissionOverwrite next)
    {
        ArgumentNullException.ThrowIfNull(actorPermissions);
        return MountPermissionOverwrite.ChangedPermissions(current, next).All(actorPermissions.Contains);
    }

    /// <summary>Space 소유자이거나 해당 Space의 ACTIVE Membership이 있으면 Mount 평가 대상이다.</summary>
    public static bool HasSpaceAccess(Space space, long userId, SpaceMembership? membership)
    {
        ArgumentNullException.ThrowIfNull(space);
        return space.OwnerUserId == userId || IsActiveMembership(space, userId, membership);
    }

    private static void ApplyImplicitPermissions(HashSet<SpacePermission> permissions)
    {
        if (!permissions.Contains(SpacePermission.MountView))
        {
            permissions.Clear();
            return;
        }

        if (!permissions.Contains(SpacePermission.FilesRead))
        {
            permissions.Remove(SpacePermission.FilesDownload);
            permissions.Remove(SpacePermission.FilesWrite);
        }
    }

    private static bool IsActiveMembership(Space space, long userId, SpaceMembership? membership) =>
        membership is not null
        && membership.Id > 0
        && membership.SpaceId == space.Id
        && membership.UserId == userId
        && membership.Status == SpaceMembershipStatus.Active;

    private static SpaceRole[] GetCurrentRoles(
        Space space,
        long userId,
        SpaceMembership? membership,
        IEnumerable<SpaceRole> roles)
    {
        if (!IsActiveMembership(space, userId, membership))
        {
            return [];
        }

        var assignedRoleIds = membership!.RoleAssignments.Select(assignment => assignment.RoleId).ToHashSet();
        return roles.Where(role => role is not null
                && role.Id > 0
                && role.SpaceId == space.Id
                && role.DeletedAt is null
                && assignedRoleIds.Contains(role.Id))
            .OrderBy(role => role.PublicId)
            .ToArray();
    }

    private static void AddSource(
        ICollection<MountPermissionSource> sources,
        MountPermissionSourceType sourceType,
        IEnumerable<SpacePermission> allow,
        IEnumerable<SpacePermission> deny,
        Guid? rolePublicId)
    {
        var source = new MountPermissionSource(sourceType, allow.ToFrozenSet(), deny.ToFrozenSet(), rolePublicId);
        if (source.Allow.Count > 0 || source.Deny.Count > 0)
        {
            sources.Add(source);
        }
    }

    private static readonly IReadOnlySet<SpacePermission> Empty = FrozenSet<SpacePermission>.Empty;
}

/// <summary>Mount 유효 권한과 계산에 적용된 근거다. 근거 순서는 적용 순서와 같다.</summary>
public sealed record MountPermissionEvaluation(
    IReadOnlySet<SpacePermission> Permissions,
    IReadOnlyList<MountPermissionSource> Sources)
{
    public static MountPermissionEvaluation None { get; } =
        new(FrozenSet<SpacePermission>.Empty, Array.Empty<MountPermissionSource>());
}

/// <summary>RolePublicId는 SourceType이 Role인 경우에만 값이 있다.</summary>
public sealed record MountPermissionSource(
    MountPermissionSourceType SourceType,
    IReadOnlySet<SpacePermission> Allow,
    IReadOnlySet<SpacePermission> Deny,
    Guid? RolePublicId);

public enum MountPermissionSourceType
{
    /// <summary>Space 소유자·ADMIN이 덮어쓰기와 무관하게 갖는 권한.</summary>
    Administrator,

    /// <summary>Space Role 권한 중 Mount 범위 권한.</summary>
    Base,

    /// <summary>PRIVATE 공개 범위로 거부된 기본 MOUNT_VIEW.</summary>
    Visibility,

    /// <summary>Role 덮어쓰기.</summary>
    Role,

    /// <summary>사용자 덮어쓰기.</summary>
    User,
}
