using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.DeletionRequests;

public sealed class RequestSpaceDeletionUseCase : IUseCase<RequestSpaceDeletionCommand, SpaceDeletionAcceptedDto>
{
    public Task<Result<SpaceDeletionAcceptedDto>> ExecuteAsync(
        RequestSpaceDeletionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RequestSpaceDeletionCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceDeletionAcceptedDto>());
        }

        throw new NotImplementedException();
    }
}
