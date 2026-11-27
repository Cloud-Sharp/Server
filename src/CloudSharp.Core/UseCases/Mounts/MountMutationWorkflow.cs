using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts;

/// <summary>
/// endpoint에서 Mount 인가를 받은 설정·상태 변경을 한 트랜잭션으로 수행한다. 권한은 다시 검사하지 않고,
/// 트랜잭션 안에서 최신 Space 상태·Mount 소속·버전만 확인한다.
/// </summary>
internal sealed class MountMutationWorkflow(
    ISpaceAuthorizationReader? authorizationReader,
    IMountReader? mountReader,
    IMountMutationWriter? writer,
    IUnitOfWork? unitOfWork,
    ITransactionExecutor? transactionExecutor,
    IClock? clock)
{
    internal async Task<Result<MountResultDto>> ExecuteAsync(
        AuthorizedMountContext context,
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
            ct => ExecuteInTransactionAsync(context, expectedVersion, domainOperation, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<MountResultDto>> ExecuteInTransactionAsync(
        AuthorizedMountContext context,
        long expectedVersion,
        Func<Mount, DateTimeOffset, Result> domainOperation,
        CancellationToken cancellationToken)
    {
        var readResult = await MountWorkflowReads.ReadActiveAsync(
            authorizationReader!, mountReader!, context, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(readResult.Errors);
        }

        var (snapshot, mount) = readResult.Value;
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

        var access = MountPermissionPolicy.Evaluate(snapshot, context.Space.UserId, mount).Permissions;
        return Result.Ok(new MountResultDto(MountDtoMapper.ToDto(snapshot.Space, mount, access), mount.Version));
    }
}

internal static class MountWorkflowReads
{
    /// <summary>
    /// 트랜잭션 안에서 최신 Space snapshot과 인가된 Mount를 다시 읽는다. Space가 ACTIVE가 아니면 상태 오류,
    /// Mount가 없거나 다른 Space로 바뀌었으면 404다.
    /// </summary>
    internal static async Task<Result<(SpaceAuthorizationSnapshot Snapshot, Mount Mount)>> ReadActiveAsync(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        AuthorizedMountContext context,
        CancellationToken cancellationToken)
    {
        var snapshotResult = await ReadActiveSpaceAsync(authorizationReader, context.Space, cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<(SpaceAuthorizationSnapshot, Mount)>(snapshotResult.Errors);
        }

        var snapshot = snapshotResult.Value;
        var mountResult = await mountReader.ReadAsync(snapshot.Space.Id, context.MountPublicId, cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<(SpaceAuthorizationSnapshot, Mount)>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.Id != context.MountId || mount.SpaceId != snapshot.Space.Id)
        {
            return Result.Fail<(SpaceAuthorizationSnapshot, Mount)>(CommonError.ResourceNotFound());
        }

        return Result.Ok((snapshot, mount));
    }

    internal static async Task<Result<SpaceAuthorizationSnapshot>> ReadActiveSpaceAsync(
        ISpaceAuthorizationReader authorizationReader,
        AuthorizedSpaceContext context,
        CancellationToken cancellationToken)
    {
        var snapshotResult = await ReadSpaceAsync(authorizationReader, context, cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return snapshotResult;
        }

        var space = snapshotResult.Value.Space;
        return space.Status == SpaceStatus.Active
            ? snapshotResult
            : Result.Fail<SpaceAuthorizationSnapshot>(SpaceAuthorizationError.StateNotAllowed(space.Status));
    }

    internal static async Task<Result<SpaceAuthorizationSnapshot>> ReadSpaceAsync(
        ISpaceAuthorizationReader authorizationReader,
        AuthorizedSpaceContext context,
        CancellationToken cancellationToken)
    {
        var snapshotResult = await authorizationReader.ReadAsync(
            context.SpacePublicId,
            context.UserId,
            cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<SpaceAuthorizationSnapshot>(snapshotResult.Errors);
        }

        var snapshot = snapshotResult.Value;
        if (snapshot is null)
        {
            return Result.Fail<SpaceAuthorizationSnapshot>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (snapshot.Space.Id != context.SpaceId || snapshot.Space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Authorization reader returned a different space.");
        }

        return Result.Ok(snapshot);
    }
}

internal static class MountDtoMapper
{
    internal static MountDto ToDto(Space space, Mount mount, IReadOnlySet<Domain.Memberships.SpacePermission> access) =>
        new(
            space.PublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            MountAccessDto.From(access, space.Status, mount.Status),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
}
