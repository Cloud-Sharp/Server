using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Removals;

/// <summary>MEMBERS_MANAGE 인가를 받은 ACTIVE Space에서 멤버를 제거한다.</summary>
public sealed record RemoveSpaceMemberCommand(
    AuthorizedSpaceContext Space,
    Guid MembershipPublicId,
    long ExpectedVersion);

internal sealed class RemoveSpaceMemberCommandValidator : AbstractValidator<RemoveSpaceMemberCommand>
{
    public RemoveSpaceMemberCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.MembershipPublicId).RequireMembershipPublicId();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
