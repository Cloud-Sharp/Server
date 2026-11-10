using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.DeletionRequests;

public sealed record RequestSpaceDeletionCommand(AuthorizedSpaceContext Space, long ExpectedVersion);

internal sealed class RequestSpaceDeletionCommandValidator : AbstractValidator<RequestSpaceDeletionCommand>
{
    public RequestSpaceDeletionCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
