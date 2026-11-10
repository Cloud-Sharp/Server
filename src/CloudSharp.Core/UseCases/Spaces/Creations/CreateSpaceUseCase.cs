using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Creations;

public sealed class CreateSpaceUseCase : IUseCase<CreateSpaceCommand, SpaceResultDto>
{
    public Task<Result<SpaceResultDto>> ExecuteAsync(
        CreateSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceResultDto>());
        }

        throw new NotImplementedException();
    }
}
