using System.Collections.Frozen;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>endpoint가 선언한 요구 Permission과 허용 Space 상태를 복사해 보관하는 인가 입력.</summary>
public sealed class SpaceAuthorizationQuery
{
    public CurrentActor Actor { get; }
    public Guid SpacePublicId { get; }
    public IReadOnlySet<SpacePermission> RequiredPermissions { get; }
    public IReadOnlySet<SpaceStatus> AllowedSpaceStatuses { get; }

    public SpaceAuthorizationQuery(
        CurrentActor actor,
        Guid spacePublicId,
        IEnumerable<SpacePermission> requiredPermissions,
        IEnumerable<SpaceStatus> allowedSpaceStatuses)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(requiredPermissions);
        ArgumentNullException.ThrowIfNull(allowedSpaceStatuses);

        var permissions = requiredPermissions.ToFrozenSet();
        foreach (var permission in permissions)
        {
            if (!Enum.IsDefined(permission))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredPermissions), permission, "Unsupported required space permission.");
            }
        }

        var statuses = allowedSpaceStatuses.ToFrozenSet();
        if (statuses.Count == 0)
        {
            throw new ArgumentException("At least one space status is required.", nameof(allowedSpaceStatuses));
        }

        foreach (var status in statuses)
        {
            if (!Enum.IsDefined(status))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(allowedSpaceStatuses), status, "Unsupported allowed space status.");
            }
        }

        Actor = actor;
        SpacePublicId = spacePublicId;
        RequiredPermissions = permissions;
        AllowedSpaceStatuses = statuses;
    }
}
