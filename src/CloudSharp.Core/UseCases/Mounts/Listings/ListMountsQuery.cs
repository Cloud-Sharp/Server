using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Listings;

/// <summary>Space 접근 인가를 받은 사용자가 MOUNT_VIEW를 가진 Mount 페이지를 조회한다.</summary>
public sealed record ListMountsQuery(
    AuthorizedSpaceContext Space,
    string? Cursor = null,
    int Limit = 50,
    string? Sort = null,
    bool Descending = true);

internal sealed class ListMountsQueryValidator : AbstractValidator<ListMountsQuery>
{
    public ListMountsQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();
        RuleFor(query => query.Cursor).ValidateCursor();
        RuleFor(query => query.Limit).RequirePageLimit();

        RuleFor(query => query.Sort)
            .Must(sort => sort is null or "CREATED_AT")
            .WithErrorCode(ErrorCodes.Mount.ListSortInvalid)
            .WithMessage("Sort must be CREATED_AT when supplied.");
    }
}
