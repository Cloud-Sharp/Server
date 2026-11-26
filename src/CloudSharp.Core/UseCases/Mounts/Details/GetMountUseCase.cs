using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Details;

public sealed class GetMountUseCase : IUseCase<GetMountQuery, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountDetailsReader DetailsReader { get; } = null!;

    public GetMountUseCase() { }

    public GetMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountDetailsReader detailsReader)
    {
        AuthorizationReader = authorizationReader;
        DetailsReader = detailsReader;
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

        if (AuthorizationReader is null || DetailsReader is null)
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
                Array.Empty<SpacePermission>(),
                new[] { SpaceStatus.Active, SpaceStatus.Archived },
                allowSystemAdminBypass: false),
            snapshotResult.Value);
        if (authorization.IsFailed)
        {
            return Result.Fail<MountResultDto>(authorization.Errors);
        }

        var authorizationSnapshot = snapshotResult.Value!;
        var detailsResult = await DetailsReader.ReadAsync(
            authorizationSnapshot.Space.Id,
            query.MountPublicId,
            cancellationToken);
        if (detailsResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(detailsResult.Errors);
        }

        var details = detailsResult.Value;
        if (details is null || details.Mount.SpaceId != authorizationSnapshot.Space.Id)
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var mount = details.Mount;
        var access = MountAccessEvaluator.Evaluate(authorizationSnapshot, query.Actor.UserId, mount);
        if (!access.Permissions.Contains(MountPermission.FilesRead))
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var dto = new MountDto(
            authorizationSnapshot.Space.PublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            details.RootFolderPublicId,
            details.UsedBytes,
            new MountAccessDto(
                access.Permissions,
                access.IsManager && authorizationSnapshot.Space.Status == SpaceStatus.Active),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
        return Result.Ok(new MountResultDto(dto, mount.Version));
    }
}
