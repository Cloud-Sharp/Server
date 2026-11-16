using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.QuotaChanges;

/// <summary>Space의 허용 용량을 변경한다. 허용 용량이 null이면 무제한이다.</summary>
public sealed record ChangeSpaceQuotaCommand(
    AuthorizedSpaceContext Space,
    long ExpectedVersion,
    long? AllowedBytes);

internal sealed class ChangeSpaceQuotaCommandValidator : AbstractValidator<ChangeSpaceQuotaCommand>
{
    public ChangeSpaceQuotaCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();

        RuleFor(command => command.AllowedBytes!.Value)
            .SetValidator(new SpaceQuotaValidator())
            .When(command => command.AllowedBytes.HasValue)
            .OverridePropertyName(nameof(ChangeSpaceQuotaCommand.AllowedBytes));
    }
}
