using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.Reactivations;

public sealed record ReactivateSpaceCommand(AuthorizedSpaceContext Space, long ExpectedVersion);

internal sealed class ReactivateSpaceCommandValidator : AbstractValidator<ReactivateSpaceCommand>
{
    public ReactivateSpaceCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
