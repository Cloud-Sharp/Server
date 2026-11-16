using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Creations;

public sealed class CreateSpaceInviteUseCase(IClock clock) : IUseCase<CreateSpaceInviteCommand, CreateSpaceInviteResultDto>
{
    public Task<Result<CreateSpaceInviteResultDto>> ExecuteAsync(
        CreateSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceInviteCommandValidator(clock).Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<CreateSpaceInviteResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<CreateSpaceInviteResultDto>>(new NotImplementedException());
    }
}
