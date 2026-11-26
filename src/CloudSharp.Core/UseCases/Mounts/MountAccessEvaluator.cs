using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;

namespace CloudSharp.Core.UseCases.Mounts;

internal sealed record MountAccessEvaluation(
    IReadOnlySet<MountPermission> Permissions,
    IReadOnlyList<MountPermissionSourceDto> Sources,
    bool IsManager);

internal static class MountAccessEvaluator
{
    public static MountAccessEvaluation Evaluate(
        SpaceAuthorizationSnapshot snapshot,
        long userId,
        Mount mount,
        MountVisibility? visibility = null) =>
        Evaluate(snapshot.Space, userId, snapshot.Membership, snapshot.Roles, mount, visibility);

    public static MountAccessEvaluation Evaluate(
        Space space,
        long userId,
        SpaceMembership? membership,
        IReadOnlyCollection<SpaceRole> roles,
        Mount mount,
        MountVisibility? visibility = null)
    {
        var hasSpaceAccess = HasSpaceAccess(space, userId, membership);
        var currentRoles = GetCurrentRoles(space, userId, membership, roles);
        var isManager = space.OwnerUserId == userId
            || currentRoles.Any(role => role.SystemKey == SpaceRoleSystemKey.Admin);
        var effectiveVisibility = visibility ?? mount.Visibility;
        var defaultPermissions = new HashSet<MountPermission>();
        if (hasSpaceAccess
            && (effectiveVisibility == MountVisibility.Public || isManager))
        {
            defaultPermissions.Add(MountPermission.FilesRead);
            defaultPermissions.Add(MountPermission.FilesDownload);
        }

        var effectivePermissions = new HashSet<MountPermission>(defaultPermissions);
        if (hasSpaceAccess)
        {
            foreach (var grant in mount.Grants.Where(grant =>
                         (grant.TargetType == MountGrantTargetType.Role
                             && currentRoles.Any(role => role.Id == grant.TargetId))
                         || (grant.TargetType == MountGrantTargetType.User && grant.TargetId == userId)))
            {
                effectivePermissions.UnionWith(grant.Permissions);
            }
        }

        if (space.Status == SpaceStatus.Archived || mount.Status == MountStatus.Archived)
        {
            effectivePermissions.Remove(MountPermission.FilesWrite);
        }

        if (!effectivePermissions.Contains(MountPermission.FilesRead))
        {
            effectivePermissions.Clear();
        }

        var sources = new List<MountPermissionSourceDto>();
        AddSource(sources, MountPermissionSourceType.Default, defaultPermissions, null, effectivePermissions);
        foreach (var role in currentRoles)
        {
            var rolePermissions = mount.Grants
                .Where(grant => grant.TargetType == MountGrantTargetType.Role && grant.TargetId == role.Id)
                .SelectMany(grant => grant.Permissions)
                .ToHashSet();
            AddSource(sources, MountPermissionSourceType.Role, rolePermissions, role.PublicId, effectivePermissions);
        }

        var userPermissions = mount.Grants
            .Where(grant => grant.TargetType == MountGrantTargetType.User && grant.TargetId == userId)
            .SelectMany(grant => grant.Permissions)
            .ToHashSet();
        AddSource(sources, MountPermissionSourceType.User, userPermissions, null, effectivePermissions);
        return new MountAccessEvaluation(effectivePermissions, sources, isManager);
    }

    public static bool HasSpaceAccess(Space space, long userId, SpaceMembership? membership) =>
        space.OwnerUserId == userId || GetActiveMembership(space, userId, membership) is not null;

    public static bool IsManager(
        Space space,
        long userId,
        SpaceMembership? membership,
        IReadOnlyCollection<SpaceRole> roles) =>
        space.OwnerUserId == userId
        || GetCurrentRoles(space, userId, membership, roles)
            .Any(role => role.SystemKey == SpaceRoleSystemKey.Admin);

    private static SpaceMembership? GetActiveMembership(
        Space space,
        long userId,
        SpaceMembership? membership) =>
        membership is not null
        && membership.Id > 0
        && membership.SpaceId == space.Id
        && membership.UserId == userId
        && membership.Status == SpaceMembershipStatus.Active
            ? membership
            : null;

    private static SpaceRole[] GetCurrentRoles(
        Space space,
        long userId,
        SpaceMembership? membership,
        IEnumerable<SpaceRole> roles)
    {
        var activeMembership = GetActiveMembership(space, userId, membership);
        if (activeMembership is null)
        {
            return [];
        }

        var assignedRoleIds = activeMembership.RoleAssignments.Select(assignment => assignment.RoleId).ToHashSet();
        return roles.Where(role => role.Id > 0
                && role.SpaceId == space.Id
                && role.DeletedAt is null
                && assignedRoleIds.Contains(role.Id))
            .OrderBy(role => role.PublicId)
            .ToArray();
    }

    private static void AddSource(
        ICollection<MountPermissionSourceDto> sources,
        MountPermissionSourceType sourceType,
        IEnumerable<MountPermission> permissions,
        Guid? rolePublicId,
        IReadOnlySet<MountPermission> effectivePermissions)
    {
        var visiblePermissions = permissions.Where(effectivePermissions.Contains).ToHashSet();
        if (visiblePermissions.Count > 0)
        {
            sources.Add(new MountPermissionSourceDto(sourceType, visiblePermissions, rolePublicId));
        }
    }
}
