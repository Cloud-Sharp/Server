using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
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

/// <summary>
/// endpoint에서 Mount의 MOUNT_MANAGE 인가를 받은 사용자가 한 대상의 덮어쓰기를 전체 교체한다.
/// 바뀌는 권한은 행위자가 해당 Mount에서 가진 권한이어야 한다.
/// </summary>
internal sealed class MountOverwriteMutationWorkflow(
    ISpaceAuthorizationReader? authorizationReader,
    IMountReader? mountReader,
    IMountCreationGrantTargetReader? targetReader,
    IMountMutationWriter? writer,
    IUnitOfWork? unitOfWork,
    ITransactionExecutor? transactionExecutor,
    IClock? clock)
{
    internal async Task<Result<MountPermissionResultDto>> ExecuteAsync(
        AuthorizedMountContext context,
        MountOverwriteTargetType targetType,
        Guid targetPublicId,
        long expectedVersion,
        IReadOnlyList<SpacePermission> allow,
        IReadOnlyList<SpacePermission> deny,
        CancellationToken cancellationToken = default)
    {
        if (authorizationReader is null || mountReader is null || targetReader is null
            || writer is null || unitOfWork is null || transactionExecutor is null || clock is null)
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.DependencyUnavailable());
        }

        return await transactionExecutor.ExecuteAsync(
            ct => ExecuteInTransactionAsync(context, targetType, targetPublicId, expectedVersion, allow, deny, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<MountPermissionResultDto>> ExecuteInTransactionAsync(
        AuthorizedMountContext context,
        MountOverwriteTargetType targetType,
        Guid targetPublicId,
        long expectedVersion,
        IReadOnlyList<SpacePermission> allow,
        IReadOnlyList<SpacePermission> deny,
        CancellationToken cancellationToken)
    {
        var readResult = await MountWorkflowReads.ReadActiveAsync(
            authorizationReader!, mountReader!, context, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(readResult.Errors);
        }

        var (snapshot, mount) = readResult.Value;
        if (mount.Version != expectedVersion)
        {
            return Result.Fail<MountPermissionResultDto>(CommonError.PreconditionFailed());
        }

        IReadOnlyCollection<Guid> rolePublicIds = targetType == MountOverwriteTargetType.Role ? [targetPublicId] : [];
        IReadOnlyCollection<Guid> userPublicIds = targetType == MountOverwriteTargetType.User ? [targetPublicId] : [];
        var targetsResult = await targetReader!.ReadAsync(
            snapshot.Space.Id,
            rolePublicIds,
            userPublicIds,
            cancellationToken);
        if (targetsResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(targetsResult.Errors);
        }

        var isRemoval = allow.Count == 0 && deny.Count == 0;
        var targetIdResult = ResolveTargetId(snapshot.Space, targetsResult.Value, targetType, targetPublicId, isRemoval);
        if (targetIdResult.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(targetIdResult.Errors);
        }

        var targetId = targetIdResult.Value;
        var next = MountPermissionOverwrite.CreateForMutation(targetType, targetId, allow, deny);
        if (next.IsFailed)
        {
            return Result.Fail<MountPermissionResultDto>(next.Errors);
        }

        if (!MountPermissionPolicy.CanChangeOverwrite(
                context.EffectivePermissions,
                mount.FindOverwrite(targetType, targetId),
                next.Value))
        {
            return Result.Fail<MountPermissionResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var observedVersion = mount.Version;
        var mutationResult = mount.SetOverwrite(targetType, targetId, allow, deny, clock!.UtcNow);
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

        var final = mount.FindOverwrite(targetType, targetId);
        var dto = new MountPermissionOverwriteDto(
            targetType,
            targetPublicId,
            final?.Allow ?? new HashSet<SpacePermission>(),
            final?.Deny ?? new HashSet<SpacePermission>());
        return Result.Ok(new MountPermissionResultDto(dto, mount.Version));
    }

    /// <summary>
    /// 같은 Space의 대상을 내부 ID로 해석한다. 덮어쓰기 설정에는 삭제되지 않은 Role 또는 활성 계정의
    /// 소유자·활성 멤버가 필요하다. 제거는 같은 Space에 있었던 삭제된 Role·비활성 멤버에도 허용한다.
    /// </summary>
    internal static Result<long> ResolveTargetId(
        Space space,
        MountCreationGrantTargets targets,
        MountOverwriteTargetType targetType,
        Guid targetPublicId,
        bool isRemoval)
    {
        if (targetType == MountOverwriteTargetType.Role)
        {
            if (!targets.Roles.TryGetValue(targetPublicId, out var role) || role.SpaceId != space.Id)
            {
                return Result.Fail<long>(CommonError.ResourceNotFound());
            }

            if (!isRemoval && role.DeletedAt is not null)
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

        if (!isRemoval
            && (user.Status != UserStatus.Active
                || (!isOwner && (membership!.Id <= 0 || membership.Status != SpaceMembershipStatus.Active))))
        {
            return Result.Fail<long>(CommonError.RequestConflict());
        }

        return Result.Ok(user.Id);
    }
}
