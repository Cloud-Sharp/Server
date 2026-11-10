using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Leaves;

public sealed class LeaveSpaceUseCase : IUseCase<LeaveSpaceCommand, LeaveSpaceResultDto>
{
    public Task<Result<LeaveSpaceResultDto>> ExecuteAsync(
        LeaveSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new LeaveSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<LeaveSpaceResultDto>());
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Task.FromResult(Result.Fail<LeaveSpaceResultDto>(actorValidation.Errors));
        }

        return Task.FromException<Result<LeaveSpaceResultDto>>(new NotImplementedException());
    }
}
