using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>조회된 snapshot으로 Session의 기본 접근 자격·Space 상태·요구 Permission을 평가하는 순수 정책.</summary>
public static class SessionSpaceAuthorizationPolicy
{
    /// <summary>조회 전에 Session actor를 검증한다. User의 최신 상태·SystemRole 검증은 인증 단계의 책임이다.</summary>
    public static Result ValidateActor(CurrentActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ActorType != ActorType.Session)
        {
            return Result.Fail(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (actor.UserId <= 0 || actor.UserPublicId == Guid.Empty
            || actor.SessionId is null || actor.SessionId == Guid.Empty || !Enum.IsDefined(actor.SystemRole))
        {
            return Result.Fail(SpaceAuthorizationError.TokenInvalid());
        }

        return Result.Ok();
    }

    public static Result<AuthorizedSpaceContext> Evaluate(
        SpaceAuthorizationQuery query,
        SpaceAuthorizationSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actorValidation = ValidateActor(query.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<AuthorizedSpaceContext>(actorValidation.Errors);
        }

        if (snapshot is null)
        {
            return Result.Fail<AuthorizedSpaceContext>(SpaceAuthorizationError.SpaceNotFound());
        }

        var space = snapshot.Space;
        if (space.PublicId != query.SpacePublicId)
        {
            throw new InvalidOperationException("Authorization reader returned a different space.");
        }

        var actor = query.Actor;
        var membership = snapshot.Membership;
        var isSystemAdmin = actor.SystemRole == SystemRole.SystemAdmin;
        var isOwner = space.OwnerUserId == actor.UserId;
        var isActiveMember = membership is not null
            && membership.Id > 0
            && membership.SpaceId == space.Id
            && membership.UserId == actor.UserId
            && membership.Status == SpaceMembershipStatus.Active;

        // 기본 접근 거부는 상태 오류보다 먼저 판단해 Space의 lifecycle 정보를 노출하지 않는다.
        if (!isSystemAdmin && !isOwner && !isActiveMember)
        {
            return Result.Fail<AuthorizedSpaceContext>(SpaceAuthorizationError.AccessDenied());
        }

        if (space.Status is SpaceStatus.Deleting or SpaceStatus.Deleted
            || !query.AllowedSpaceStatuses.Contains(space.Status))
        {
            return Result.Fail<AuthorizedSpaceContext>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var source = isSystemAdmin
            ? SpaceAuthorizationSource.SystemAdminBypass
            : isOwner ? SpaceAuthorizationSource.Owner : SpaceAuthorizationSource.Membership;
        var effective = isSystemAdmin
            ? Enum.GetValues<SpacePermission>().ToHashSet()
            : SpacePermissionPolicy.Evaluate(space, actor.UserId, membership, snapshot.Roles);

        if (!SpacePermissionPolicy.HasAll(effective, query.RequiredPermissions))
        {
            return Result.Fail<AuthorizedSpaceContext>(SpaceAuthorizationError.PermissionDenied());
        }

        return Result.Ok(new AuthorizedSpaceContext(
            space.Id, space.PublicId, actor.ActorType, actor.UserId,
            source == SpaceAuthorizationSource.Membership ? membership!.Id : null,
            effective, source));
    }
}
