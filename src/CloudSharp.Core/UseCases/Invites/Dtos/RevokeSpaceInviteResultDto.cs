namespace CloudSharp.Core.UseCases.Invites.Dtos;

/// <summary>초대 폐기 성공 응답의 ETag로 변환할 버전이다.</summary>
public sealed record RevokeSpaceInviteResultDto(long Version);
