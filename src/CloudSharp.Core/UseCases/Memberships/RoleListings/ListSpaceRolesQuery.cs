using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.RoleListings;

/// <summary>MEMBERS_VIEW 인가를 받은 ACTIVE·ARCHIVED Space의 관리형 Role을 조회한다.</summary>
public sealed record ListSpaceRolesQuery(AuthorizedSpaceContext Space);

internal sealed class ListSpaceRolesQueryValidator : AbstractValidator<ListSpaceRolesQuery>
{
    public ListSpaceRolesQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();
    }
}
