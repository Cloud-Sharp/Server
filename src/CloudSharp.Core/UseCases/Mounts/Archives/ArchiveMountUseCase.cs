using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Archives;

public sealed class ArchiveMountUseCase : IUseCase<ArchiveMountCommand, MountResultDto>
{
    public Task<Result<MountResultDto>> ExecuteAsync(
        ArchiveMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ArchiveMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountResultDto>());
        }

        throw new NotImplementedException();
    }
}
