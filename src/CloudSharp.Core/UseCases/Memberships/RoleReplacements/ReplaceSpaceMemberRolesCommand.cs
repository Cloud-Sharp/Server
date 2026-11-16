using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.RoleReplacements;

/// <summary>MEMBERS_MANAGE 인가를 받은 ACTIVE Space에서 멤버의 Role 집합을 교체한다.</summary>
public sealed record ReplaceSpaceMemberRolesCommand(
    AuthorizedSpaceContext Space,
    Guid MembershipPublicId,
    long ExpectedVersion,
    IReadOnlyList<Guid> RolePublicIds);

internal sealed class ReplaceSpaceMemberRolesCommandValidator : AbstractValidator<ReplaceSpaceMemberRolesCommand>
{
    public ReplaceSpaceMemberRolesCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.MembershipPublicId).RequireMembershipPublicId();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();

        RuleFor(command => command.RolePublicIds)
            .Must(ids => ids is { Count: > 0 }
                         && ids.All(MembershipInputValidationRules.IsPublicId)
                         && ids.Distinct().Count() == ids.Count)
            .WithErrorCode(ErrorCodes.SpaceMembership.RoleInvalid)
            .WithMessage("Role public IDs must be a non-empty list of unique RFC-variant UUIDv7 values.");
    }
}
