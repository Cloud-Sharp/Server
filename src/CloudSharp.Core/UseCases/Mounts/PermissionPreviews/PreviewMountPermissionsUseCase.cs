using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionPreviews;

public sealed class PreviewMountPermissionsUseCase : IUseCase<PreviewMountPermissionsQuery, MountPermissionPreviewPageDto>
{
    public Task<Result<MountPermissionPreviewPageDto>> ExecuteAsync(
        PreviewMountPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new PreviewMountPermissionsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPermissionPreviewPageDto>());
        }

        throw new NotImplementedException();
    }
}
