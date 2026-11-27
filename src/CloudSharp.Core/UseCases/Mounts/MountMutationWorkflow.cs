using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts;

internal sealed class MountMutationWorkflow(
    ISpaceAuthorizationReader? authorizationReader,
    IMountReader? mountReader,
    IMountMutationWriter? writer,
    IUnitOfWork? unitOfWork,
    ITransactionExecutor? transactionExecutor,
    IClock? clock)
{
    internal async Task<Result<MountResultDto>> ExecuteAsync(
        CurrentActor actor,
        Guid spacePublicId,
        Guid mountPublicId,
        long expectedVersion,
        Func<Mount, DateTimeOffset, Result> domainOperation,
        CancellationToken cancellationToken = default)
    {
        if (authorizationReader is null || mountReader is null || writer is null
            || unitOfWork is null || transactionExecutor is null || clock is null)
        {
            return Result.Fail<MountResultDto>(CommonError.DependencyUnavailable());
        }

        return await transactionExecutor.ExecuteAsync(
            ct => ExecuteInTransactionAsync(
                actor,
                spacePublicId,
                mountPublicId,
                expectedVersion,
                domainOperation,
                ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<MountResultDto>> ExecuteInTransactionAsync(
        CurrentActor actor,
        Guid spacePublicId,
        Guid mountPublicId,
        long expectedVersion,
        Func<Mount, DateTimeOffset, Result> domainOperation,
        CancellationToken cancellationToken)
    {
        var authorizationResult = await authorizationReader!.ReadAsync(
            spacePublicId,
            actor.UserId,
            cancellationToken);
        if (authorizationResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(authorizationResult.Errors);
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
            return Result.Fail<MountResultDto>(authorization.Errors);
        }

        var authorizationSnapshot = authorizationResult.Value!;
        var mountResult = await mountReader!.ReadAsync(
            authorizationSnapshot.Space.Id,
            mountPublicId,
            cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.SpaceId != authorizationSnapshot.Space.Id)
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var access = MountAccessEvaluator.Evaluate(authorizationSnapshot, actor.UserId, mount);
        if (!access.Permissions.Contains(MountPermission.FilesRead))
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        if (!access.IsManager)
        {
            return Result.Fail<MountResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (mount.Version != expectedVersion)
        {
            return Result.Fail<MountResultDto>(CommonError.PreconditionFailed());
        }

        var observedVersion = mount.Version;
        var mutationResult = domainOperation(mount, clock!.UtcNow);
        if (mutationResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(mutationResult.Errors);
        }

        if (mount.Version != observedVersion)
        {
            var updateResult = await writer!.UpdateAsync(mount, observedVersion, cancellationToken);
            if (updateResult.IsFailed)
            {
                return Result.Fail<MountResultDto>(updateResult.Errors);
            }

            var saveResult = await unitOfWork!.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                return Result.Fail<MountResultDto>(saveResult.Errors);
            }
        }

        access = MountAccessEvaluator.Evaluate(authorizationSnapshot, actor.UserId, mount);
        var dto = new MountDto(
            authorizationSnapshot.Space.PublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            new MountAccessDto(
                access.Permissions,
                access.IsManager && authorizationSnapshot.Space.Status == SpaceStatus.Active),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
        return Result.Ok(new MountResultDto(dto, mount.Version));
    }
}
