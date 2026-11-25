using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.Details;

/// <summary>INVITES_MANAGE 인가를 받은 ACTIVE·ARCHIVED Space의 초대와 현재 버전을 조회한다.</summary>
public sealed record GetSpaceInviteQuery(AuthorizedSpaceContext Space, Guid InvitePublicId);

internal sealed class GetSpaceInviteQueryValidator : AbstractValidator<GetSpaceInviteQuery>
{
    public GetSpaceInviteQueryValidator()
    {
        RuleFor(query => query.Space).RequireAuthorizedSpaceContext();

        RuleFor(query => query.InvitePublicId)
            .Must(id => id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithErrorCode(ErrorCodes.SpaceInvite.PublicIdInvalid)
            .WithMessage("Invite public ID must be an RFC-variant UUIDv7.");
    }
}
