using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// 조회된 Space snapshot과 Mount로 Session의 Mount 접근을 평가하는 순수 정책. 검사 순서는 Space 접근 자격,
/// Space 상태, Mount 소속, 최종 MOUNT_VIEW, Mount 상태, 요구 권한이다. MOUNT_VIEW가 없으면 Mount가 없는
/// 것과 같은 <see cref="CommonError.ResourceNotFound"/>를 반환한다.
/// </summary>
public static class MountAuthorizationPolicy
{
    public static Result<AuthorizedMountContext> Evaluate(
        MountAuthorizationQuery query,
        SpaceAuthorizationSnapshot? snapshot,
        Mount? mount)
    {
        ArgumentNullException.ThrowIfNull(query);

        var spaceAuthorization = SessionSpaceAuthorizationPolicy.Evaluate(
            new SpaceAuthorizationQuery(
                query.Actor,
                query.SpacePublicId,
                Array.Empty<SpacePermission>(),
                query.AllowedSpaceStatuses,
                allowSystemAdminBypass: false),
            snapshot);
        if (spaceAuthorization.IsFailed)
        {
            return Result.Fail<AuthorizedMountContext>(spaceAuthorization.Errors);
        }

        if (mount is null || mount.SpaceId != snapshot!.Space.Id || mount.PublicId != query.MountPublicId)
        {
            return Result.Fail<AuthorizedMountContext>(CommonError.ResourceNotFound());
        }

        var permissions = MountPermissionPolicy.Evaluate(snapshot, query.Actor.UserId, mount).Permissions;
        if (!permissions.Contains(SpacePermission.MountView))
        {
            return Result.Fail<AuthorizedMountContext>(CommonError.ResourceNotFound());
        }

        if (!query.AllowedMountStatuses.Contains(mount.Status))
        {
            return Result.Fail<AuthorizedMountContext>(CommonError.RequestConflict());
        }

        if (!query.RequiredPermissions.All(permissions.Contains))
        {
            return Result.Fail<AuthorizedMountContext>(SpaceAuthorizationError.PermissionDenied());
        }

        return Result.Ok(new AuthorizedMountContext(
            spaceAuthorization.Value,
            mount.Id,
            mount.PublicId,
            permissions));
    }
}
