using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Revocations;

public sealed class RevokeSpaceInviteUseCase : IUseCase<RevokeSpaceInviteCommand, RevokeSpaceInviteResultDto>
{
    public Task<Result<RevokeSpaceInviteResultDto>> ExecuteAsync(
        RevokeSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RevokeSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<RevokeSpaceInviteResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<RevokeSpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<RevokeSpaceInviteResultDto>>(new NotImplementedException());
    }
}
