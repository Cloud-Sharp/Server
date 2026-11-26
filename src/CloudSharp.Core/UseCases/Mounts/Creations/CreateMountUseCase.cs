using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

public sealed class CreateMountUseCase : IUseCase<CreateMountCommand, MountResultDto>
{
    public Task<Result<MountResultDto>> ExecuteAsync(
        CreateMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountResultDto>());
        }

        throw new NotImplementedException();
    }
}
