using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.MetadataUpdates;

public sealed class UpdateSpaceUseCase : IUseCase<UpdateSpaceCommand, SpaceResultDto>
{
    public Task<Result<SpaceResultDto>> ExecuteAsync(
        UpdateSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new UpdateSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceResultDto>());
        }

        throw new NotImplementedException();
    }
}
