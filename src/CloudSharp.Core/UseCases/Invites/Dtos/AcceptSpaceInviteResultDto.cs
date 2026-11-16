namespace CloudSharp.Core.UseCases.Invites.Dtos;

/// <summary>초대 수락 결과다. 기존 OWNER의 MembershipPublicId는 null이다.</summary>
public sealed record AcceptSpaceInviteResultDto(
    Guid SpacePublicId,
    Guid? MembershipPublicId,
    bool AlreadyMember);
