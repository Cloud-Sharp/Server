using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Details;

public sealed class GetMountUseCase : IUseCase<GetMountQuery, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountReader MountReader { get; } = null!;

    public GetMountUseCase() { }

    public GetMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader)
    {
        AuthorizationReader = authorizationReader;
        MountReader = mountReader;
    }

    public async Task<Result<MountResultDto>> ExecuteAsync(
        GetMountQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetMountQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        if (AuthorizationReader is null || MountReader is null)
        {
            return Result.Fail<MountResultDto>(CommonError.DependencyUnavailable());
        }

        var snapshotResult = await AuthorizationReader.ReadAsync(
            query.SpacePublicId,
            query.Actor.UserId,
            cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(snapshotResult.Errors);
        }

        var authorization = SessionSpaceAuthorizationPolicy.Evaluate(
            new SpaceAuthorizationQuery(
                query.Actor,
                query.SpacePublicId,
                [],
                [SpaceStatus.Active, SpaceStatus.Archived],
                allowSystemAdminBypass: false),
            snapshotResult.Value);
        if (authorization.IsFailed)
        {
            return Result.Fail<MountResultDto>(authorization.Errors);
        }

        var spaceSnapshot = snapshotResult.Value!;
        var space = spaceSnapshot.Space;
        var mountResult = await MountReader.ReadAsync(space.Id, query.MountPublicId, cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.SpaceId != space.Id)
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var access = MountAccessEvaluator.Evaluate(spaceSnapshot, query.Actor.UserId, mount);
        if (!access.Permissions.Contains(MountPermission.FilesRead))
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var dto = new MountDto(
            space.PublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            new MountAccessDto(
                access.Permissions,
                access.IsManager && space.Status == SpaceStatus.Active),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
        return Result.Ok(new MountResultDto(dto, mount.Version));
    }
}
