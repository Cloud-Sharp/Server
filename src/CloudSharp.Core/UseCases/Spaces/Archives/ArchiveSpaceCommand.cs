using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.Archives;

public sealed record ArchiveSpaceCommand(AuthorizedSpaceContext Space, long ExpectedVersion);

internal sealed class ArchiveSpaceCommandValidator : AbstractValidator<ArchiveSpaceCommand>
{
    public ArchiveSpaceCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
