using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.PublicInfo;

/// <summary>초대 토큰으로 공개 가입 정보를 조회한다. 익명 호출을 허용한다.</summary>
public sealed record GetPublicSpaceInviteQuery(string InviteToken)
{
    public override string ToString() => "GetPublicSpaceInviteQuery { InviteToken = [redacted] }";
}

internal sealed class GetPublicSpaceInviteQueryValidator : AbstractValidator<GetPublicSpaceInviteQuery>
{
    public GetPublicSpaceInviteQueryValidator()
    {
        RuleFor(query => query.InviteToken).RequireInviteToken();
    }
}
