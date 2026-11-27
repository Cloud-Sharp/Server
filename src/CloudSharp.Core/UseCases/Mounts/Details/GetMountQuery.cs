using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Details;

/// <summary>MOUNT_VIEW 인가를 받은 Mount의 상세와 현재 버전을 조회한다.</summary>
public sealed record GetMountQuery(AuthorizedMountContext Mount);

internal sealed class GetMountQueryValidator : AbstractValidator<GetMountQuery>
{
    public GetMountQueryValidator()
    {
        RuleFor(query => query.Mount).RequireAuthorizedMountContext();
    }
}
