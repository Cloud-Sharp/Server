using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Acceptances;

public sealed class AcceptSpaceInviteUseCase : IUseCase<AcceptSpaceInviteCommand, AcceptSpaceInviteResultDto>
{
    public Task<Result<AcceptSpaceInviteResultDto>> ExecuteAsync(
        AcceptSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new AcceptSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<AcceptSpaceInviteResultDto>());
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Task.FromResult(Result.Fail<AcceptSpaceInviteResultDto>(actorValidation.Errors));
        }

        return Task.FromException<Result<AcceptSpaceInviteResultDto>>(new NotImplementedException());
    }
}
