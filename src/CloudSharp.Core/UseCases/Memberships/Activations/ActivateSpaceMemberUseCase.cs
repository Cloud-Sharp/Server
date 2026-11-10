using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Activations;

public sealed class ActivateSpaceMemberUseCase : IUseCase<ActivateSpaceMemberCommand, SpaceMembershipResultDto>
{
    public Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        ActivateSpaceMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ActivateSpaceMemberCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceMembershipResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<SpaceMembershipResultDto>>(new NotImplementedException());
    }
}
