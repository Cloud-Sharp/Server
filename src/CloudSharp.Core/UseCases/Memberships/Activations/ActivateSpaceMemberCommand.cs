using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Activations;

/// <summary>MEMBERS_MANAGE 인가를 받은 ACTIVE Space에서 정지된 멤버를 활성화한다.</summary>
public sealed record ActivateSpaceMemberCommand(
    AuthorizedSpaceContext Space,
    Guid MembershipPublicId,
    long ExpectedVersion);

internal sealed class ActivateSpaceMemberCommandValidator : AbstractValidator<ActivateSpaceMemberCommand>
{
    public ActivateSpaceMemberCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.MembershipPublicId).RequireMembershipPublicId();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
