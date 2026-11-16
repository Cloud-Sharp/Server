using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.Details;

public sealed record GetSpaceQuery(AuthorizedSpaceContext Space);

internal sealed class GetSpaceQueryValidator : AbstractValidator<GetSpaceQuery>
{
    public GetSpaceQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();
    }
}
