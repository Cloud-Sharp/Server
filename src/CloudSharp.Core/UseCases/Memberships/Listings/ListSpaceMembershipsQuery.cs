using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Listings;

/// <summary>MEMBERS_VIEW 인가를 받은 ACTIVE·ARCHIVED Space의 멤버를 페이지로 조회한다.</summary>
public sealed record ListSpaceMembershipsQuery(
    AuthorizedSpaceContext Space,
    string? Cursor = null,
    int Limit = 50,
    string? Sort = null,
    bool Descending = true);

internal sealed class ListSpaceMembershipsQueryValidator : AbstractValidator<ListSpaceMembershipsQuery>
{
    public ListSpaceMembershipsQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, 100)
            .WithErrorCode(ErrorCodes.Space.ListLimitInvalid)
            .WithMessage("Limit must be between 1 and 100.");

        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || !string.IsNullOrWhiteSpace(cursor))
            .WithErrorCode(ErrorCodes.Space.ListCursorInvalid)
            .WithMessage("Cursor must not be empty or whitespace.");

        RuleFor(query => query.Sort)
            .Must(sort => sort is null
                || string.Equals(sort, "joinedAt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sort, "updatedAt", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode(ErrorCodes.Space.ListSortInvalid)
            .WithMessage("Sort must be joinedAt or updatedAt.");
    }
}
