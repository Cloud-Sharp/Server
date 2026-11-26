using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts;

internal sealed class MountGrantMutationWorkflow(
    ISpaceAuthorizationReader? authorizationReader,
    IMountReader? mountReader,
    IMountCreationGrantTargetReader? grantTargetReader,
    IMountMutationWriter? writer,
    IUnitOfWork? unitOfWork,
    ITransactionExecutor? transactionExecutor,
    IClock? clock)
{
    internal async Task<Result<MountPermissionResultDto>> ExecuteAsync(
        CurrentActor actor,
        Guid spacePublicId,
        Guid mountPublicId,
        MountGrantTargetType targetType,
        Guid targetPublicId,
        long expectedVersion,
        IReadOnlyList<MountPermission> permissions,
        CancellationToken cancellationToken = default)
    {
        if (authorizationReader is null || mountReader is null || grantTargetReader is null
            || writer is null || unitOfWork is null || transactionExecutor is null || clock is null)
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.DependencyUnavailable());
        }

        return await transactionExecutor.ExecuteAsync(
            ct => ExecuteInTransactionAsync(
                actor,
                spacePublicId,
                mountPublicId,
                targetType,
                targetPublicId,
                expectedVersion,
                permissions,
                ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<MountPermissionResultDto>> ExecuteInTransactionAsync(
        CurrentActor actor,
        Guid spacePublicId,
        Guid mountPublicId,
        MountGrantTargetType targetType,
        Guid targetPublicId,
        long expectedVersion,
        IReadOnlyList<MountPermission> permissions,
        CancellationToken cancellationToken)
    {
        var authorizationResult = await authorizationReader!.ReadAsync(
            spacePublicId,
            actor.UserId,
            cancellationToken);
        if (authorizationResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(authorizationResult.Errors);
        }

        var authorization = SessionSpaceAuthorizationPolicy.Evaluate(
            new SpaceAuthorizationQuery(
                actor,
                spacePublicId,
                Array.Empty<SpacePermission>(),
                [SpaceStatus.Active],
                allowSystemAdminBypass: false),
            authorizationResult.Value);
        if (authorization.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(authorization.Errors);
        }

        var authorizationSnapshot = authorizationResult.Value!;
        var mountResult = await mountReader!.ReadAsync(
            authorizationSnapshot.Space.Id,
            mountPublicId,
            cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.SpaceId != authorizationSnapshot.Space.Id)
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.ResourceNotFound());
        }

        var access = MountAccessEvaluator.Evaluate(authorizationSnapshot, actor.UserId, mount);
        if (!access.Permissions.Contains(MountPermission.FilesRead))
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.ResourceNotFound());
        }

        if (!access.IsManager)
        {
            return Result.Fail<MountPermissionResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (mount.Version != expectedVersion)
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.PreconditionFailed());
        }

        IReadOnlyCollection<Guid> rolePublicIds = targetType == MountGrantTargetType.Role
            ? [targetPublicId]
            : [];
        IReadOnlyCollection<Guid> userPublicIds = targetType == MountGrantTargetType.User
            ? [targetPublicId]
            : [];
        var targetsResult = await grantTargetReader!.ReadAsync(
            authorizationSnapshot.Space.Id,
            rolePublicIds,
            userPublicIds,
            cancellationToken);
        if (targetsResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(targetsResult.Errors);
        }

        var targetIdResult = ResolveTargetId(
            authorizationSnapshot.Space,
            targetsResult.Value,
            targetType,
            targetPublicId,
            permissions);
        if (targetIdResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(targetIdResult.Errors);
        }

        var observedVersion = mount.Version;
        var mutationResult = mount.SetGrant(
            targetType,
            targetIdResult.Value,
            permissions,
            clock!.UtcNow);
        if (mutationResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(mutationResult.Errors);
        }

        if (mount.Version != observedVersion)
        {
            var updateResult = await writer!.UpdateAsync(mount, observedVersion, cancellationToken);
            if (updateResult.IsFailed)
            {
                return Result.Fail<MountPermissionResultDto>(updateResult.Errors);
            }

            var saveResult = await unitOfWork!.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                return Result.Fail<MountPermissionResultDto>(saveResult.Errors);
            }
        }

        var finalGrant = mount.Grants.SingleOrDefault(grant =>
            grant.TargetType == targetType && grant.TargetId == targetIdResult.Value);
        IReadOnlySet<MountPermission> finalPermissions =
            finalGrant?.Permissions ?? new HashSet<MountPermission>();
        var grantDto = new MountPermissionGrantDto(targetType, targetPublicId, finalPermissions);
        return Result.Ok(new MountPermissionResultDto(grantDto, mount.Version));
    }

    private static Result<long> ResolveTargetId(
        Space space,
        MountCreationGrantTargets targets,
        MountGrantTargetType targetType,
        Guid targetPublicId,
        IReadOnlyList<MountPermission> permissions)
    {
        if (targetType == MountGrantTargetType.Role)
        {
            if (!targets.Roles.TryGetValue(targetPublicId, out var role) || role.SpaceId != space.Id)
            {
                return Result.Fail<long>(CommonError.ResourceNotFound());
            }

            if (permissions.Count > 0 && role.DeletedAt is not null)
            {
                return Result.Fail<long>(CommonError.RequestConflict());
            }

            return Result.Ok(role.Id);
        }

        if (!targets.Users.TryGetValue(targetPublicId, out var target))
        {
            return Result.Fail<long>(CommonError.ResourceNotFound());
        }

        var user = target.User;
        var membership = target.Membership;
        var isOwner = user.Id == space.OwnerUserId;
        var hasSameSpaceMembership = membership is not null
            && membership.SpaceId == space.Id
            && membership.UserId == user.Id;
        if (!isOwner && !hasSameSpaceMembership)
        {
            return Result.Fail<long>(CommonError.ResourceNotFound());
        }

        if (permissions.Count > 0
            && (user.Status != UserStatus.Active
                || (!isOwner && (membership!.Id <= 0 || membership.Status != SpaceMembershipStatus.Active))))
        {
            return Result.Fail<long>(CommonError.RequestConflict());
        }

        return Result.Ok(user.Id);
    }
}
