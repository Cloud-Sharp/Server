using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

public sealed class ListMountPermissionsUseCase : IUseCase<ListMountPermissionsQuery, MountPermissionPageDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountReader MountReader { get; } = null!;
    private IMountGrantReader GrantReader { get; } = null!;

    public ListMountPermissionsUseCase() { }

    internal ListMountPermissionsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountGrantReader grantReader)
    {
        AuthorizationReader = authorizationReader;
        MountReader = mountReader;
        GrantReader = grantReader;
    }

    public Task<Result<MountPermissionPageDto>> ExecuteAsync(
        ListMountPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountPermissionsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPermissionPageDto>());
        }

        throw new NotImplementedException();
    }
}
