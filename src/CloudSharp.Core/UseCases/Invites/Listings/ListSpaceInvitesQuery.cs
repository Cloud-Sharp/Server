using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.Listings;

/// <summary>INVITES_MANAGE 인가를 받은 ACTIVE·ARCHIVED Space의 초대 목록을 조회한다.</summary>
public sealed record ListSpaceInvitesQuery(
    AuthorizedSpaceContext Space,
    string? Cursor = null,
    int Limit = 50,
    string? Sort = null,
    bool Descending = true);

internal sealed class ListSpaceInvitesQueryValidator : AbstractValidator<ListSpaceInvitesQuery>
{
    public ListSpaceInvitesQueryValidator()
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
            .Must(sort => sort is null || !string.IsNullOrWhiteSpace(sort))
            .WithErrorCode(ErrorCodes.Space.ListSortInvalid)
            .WithMessage("Sort must not be empty or whitespace.");
    }
}
