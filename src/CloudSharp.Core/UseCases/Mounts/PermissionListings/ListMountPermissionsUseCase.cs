using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

public sealed class ListMountPermissionsUseCase : IUseCase<ListMountPermissionsQuery, MountPermissionPageDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountReader MountReader { get; } = null!;
    private IMountGrantReader GrantReader { get; } = null!;

    public ListMountPermissionsUseCase() { }

    public ListMountPermissionsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountGrantReader grantReader)
    {
        AuthorizationReader = authorizationReader;
        MountReader = mountReader;
        GrantReader = grantReader;
    }

    public async Task<Result<MountPermissionPageDto>> ExecuteAsync(
        ListMountPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountPermissionsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountPermissionPageDto>();
        }

        if (AuthorizationReader is null || MountReader is null || GrantReader is null)
        {
            return Result.Fail<MountPermissionPageDto>(CommonError.DependencyUnavailable());
        }

        var snapshotResult = await AuthorizationReader.ReadAsync(
            query.SpacePublicId,
            query.Actor.UserId,
            cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountPermissionPageDto>(snapshotResult.Errors);
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
            return Result.Fail<MountPermissionPageDto>(authorization.Errors);
        }

        var authorizationSnapshot = snapshotResult.Value!;
        var space = authorizationSnapshot.Space;
        var mountResult = await MountReader.ReadAsync(space.Id, query.MountPublicId, cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountPermissionPageDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.SpaceId != space.Id)
        {
            return Result.Fail<MountPermissionPageDto>(CommonError.ResourceNotFound());
        }

        var access = MountAccessEvaluator.Evaluate(authorizationSnapshot, query.Actor.UserId, mount);
        if (!access.Permissions.Contains(MountPermission.FilesRead))
        {
            return Result.Fail<MountPermissionPageDto>(CommonError.ResourceNotFound());
        }

        if (!access.IsManager)
        {
            return Result.Fail<MountPermissionPageDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var grantsResult = await GrantReader.ReadAsync(
            space.Id,
            mount.Id,
            query.TargetType,
            query.Cursor,
            query.Limit,
            cancellationToken);
        if (grantsResult.IsFailed)
        {
            return Result.Fail<MountPermissionPageDto>(grantsResult.Errors);
        }

        var page = grantsResult.Value;
        var items = page.Grants.Select(snapshot => new MountPermissionGrantDto(
            snapshot.Grant.TargetType,
            snapshot.TargetPublicId,
            snapshot.Grant.Permissions)).ToArray();
        return Result.Ok(new MountPermissionPageDto(items, page.NextCursor));
    }
}
