using System.Collections.Frozen;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>endpoint가 선언한 Mount 범위 요구 권한과 허용 Space·Mount 상태를 복사해 보관하는 인가 입력.</summary>
public sealed class MountAuthorizationQuery
{
    public CurrentActor Actor { get; }
    public Guid SpacePublicId { get; }
    public Guid MountPublicId { get; }
    public IReadOnlySet<SpacePermission> RequiredPermissions { get; }
    public IReadOnlySet<SpaceStatus> AllowedSpaceStatuses { get; }
    public IReadOnlySet<MountStatus> AllowedMountStatuses { get; }

    public MountAuthorizationQuery(
        CurrentActor actor,
        Guid spacePublicId,
        Guid mountPublicId,
        IEnumerable<SpacePermission> requiredPermissions,
        IEnumerable<SpaceStatus> allowedSpaceStatuses,
        IEnumerable<MountStatus> allowedMountStatuses)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(requiredPermissions);
        ArgumentNullException.ThrowIfNull(allowedSpaceStatuses);
        ArgumentNullException.ThrowIfNull(allowedMountStatuses);

        var permissions = requiredPermissions.ToFrozenSet();
        foreach (var permission in permissions)
        {
            if (!MountScopedPermissions.Contains(permission))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredPermissions), permission, "Required permission must be mount-scoped.");
            }
        }

        var spaceStatuses = allowedSpaceStatuses.ToFrozenSet();
        if (spaceStatuses.Count == 0 || spaceStatuses.Any(status => !Enum.IsDefined(status)))
        {
            throw new ArgumentException(
                "At least one defined space status is required.", nameof(allowedSpaceStatuses));
        }

        var mountStatuses = allowedMountStatuses.ToFrozenSet();
        if (mountStatuses.Count == 0 || mountStatuses.Any(status => !Enum.IsDefined(status)))
        {
            throw new ArgumentException(
                "At least one defined mount status is required.", nameof(allowedMountStatuses));
        }

        Actor = actor;
        SpacePublicId = spacePublicId;
        MountPublicId = mountPublicId;
        RequiredPermissions = permissions;
        AllowedSpaceStatuses = spaceStatuses;
        AllowedMountStatuses = mountStatuses;
    }
}
