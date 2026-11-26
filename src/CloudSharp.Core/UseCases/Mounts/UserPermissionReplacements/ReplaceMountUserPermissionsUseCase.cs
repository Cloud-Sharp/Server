using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;

public sealed class ReplaceMountUserPermissionsUseCase
    : IUseCase<ReplaceMountUserPermissionsCommand, MountPermissionResultDto>
{
    public Task<Result<MountPermissionResultDto>> ExecuteAsync(
        ReplaceMountUserPermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReplaceMountUserPermissionsCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPermissionResultDto>());
        }

        throw new NotImplementedException();
    }
}
