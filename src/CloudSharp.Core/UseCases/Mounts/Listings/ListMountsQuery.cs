using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Listings;

/// <summary>Session 사용자가 읽을 수 있는 Mount 페이지를 조회한다.</summary>
public sealed record ListMountsQuery(
    CurrentActor Actor,
    Guid SpacePublicId,
    string? Cursor = null,
    int Limit = 50,
    string? Sort = null,
    bool Descending = true);

internal sealed class ListMountsQueryValidator : AbstractValidator<ListMountsQuery>
{
    public ListMountsQueryValidator()
    {
        RuleFor(query => query.Actor).RequireSessionActor();
        RuleFor(query => query.SpacePublicId).RequireSpacePublicId();
        RuleFor(query => query.Cursor).ValidateCursor();
        RuleFor(query => query.Limit).RequirePageLimit();

        RuleFor(query => query.Sort)
            .Must(sort => sort is null or "CREATED_AT")
            .WithErrorCode(ErrorCodes.Mount.ListSortInvalid)
            .WithMessage("Sort must be CREATED_AT when supplied.");
    }
}
