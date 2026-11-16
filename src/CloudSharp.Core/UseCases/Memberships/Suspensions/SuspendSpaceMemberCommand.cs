using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Suspensions;

/// <summary>MEMBERS_MANAGE 인가를 받은 ACTIVE Space에서 멤버를 정지한다.</summary>
public sealed record SuspendSpaceMemberCommand(
    AuthorizedSpaceContext Space,
    Guid MembershipPublicId,
    long ExpectedVersion);

internal sealed class SuspendSpaceMemberCommandValidator : AbstractValidator<SuspendSpaceMemberCommand>
{
    public SuspendSpaceMemberCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.MembershipPublicId).RequireMembershipPublicId();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
