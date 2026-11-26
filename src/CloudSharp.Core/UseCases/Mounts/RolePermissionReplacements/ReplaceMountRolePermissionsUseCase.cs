using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;

public sealed class ReplaceMountRolePermissionsUseCase
    : IUseCase<ReplaceMountRolePermissionsCommand, MountPermissionResultDto>
{
    public Task<Result<MountPermissionResultDto>> ExecuteAsync(
        ReplaceMountRolePermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReplaceMountRolePermissionsCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPermissionResultDto>());
        }

        throw new NotImplementedException();
    }
}
