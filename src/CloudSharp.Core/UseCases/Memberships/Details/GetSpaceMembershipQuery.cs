using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Details;

/// <summary>MEMBERS_VIEW 인가를 받은 ACTIVE·ARCHIVED Space의 멤버와 버전을 조회한다.</summary>
public sealed record GetSpaceMembershipQuery(AuthorizedSpaceContext Space, Guid MembershipPublicId);

internal sealed class GetSpaceMembershipQueryValidator : AbstractValidator<GetSpaceMembershipQuery>
{
    public GetSpaceMembershipQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();
        RuleFor(query => query.MembershipPublicId).RequireMembershipPublicId();
    }
}
