using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.Listings;

/// <summary>인증된 행위자가 접근할 수 있는 Space의 페이지를 조회한다.</summary>
public sealed record ListSpacesQuery(
    CurrentActor Actor,
    string? Cursor = null,
    int Limit = 50,
    string? Sort = null,
    bool Descending = true);

internal sealed class ListSpacesQueryValidator : AbstractValidator<ListSpacesQuery>
{
    public ListSpacesQueryValidator()
    {
        RuleFor(query => query.Actor)
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.ActorRequired)
            .WithMessage("Actor is required.");

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
