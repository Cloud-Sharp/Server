namespace CloudSharp.Core.UseCases.Invites.Dtos;

/// <summary>유효한 초대 링크로 공개하는 가입 정보다.</summary>
public sealed record PublicSpaceInviteDto(string SpaceName, string RoleName, DateTimeOffset ExpiresAt);
