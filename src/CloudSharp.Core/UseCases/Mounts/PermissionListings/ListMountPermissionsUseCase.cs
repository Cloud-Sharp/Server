using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

public sealed class ListMountPermissionsUseCase : IUseCase<ListMountPermissionsQuery, MountPermissionPageDto>
{
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
