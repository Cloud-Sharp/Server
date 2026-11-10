using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.QuotaReads;

public sealed record GetSpaceQuotaQuery(AuthorizedSpaceContext Space);

internal sealed class GetSpaceQuotaQueryValidator : AbstractValidator<GetSpaceQuotaQuery>
{
    public GetSpaceQuotaQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();
    }
}
