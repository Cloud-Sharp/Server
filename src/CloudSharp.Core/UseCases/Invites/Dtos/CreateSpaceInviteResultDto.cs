namespace CloudSharp.Core.UseCases.Invites.Dtos;

/// <summary>생성 시 한 번만 전달하는 초대 token과 URL, 초대 정책과 ETag로 변환할 버전이다.</summary>
public sealed record CreateSpaceInviteResultDto(
    Guid InvitePublicId,
    string InviteToken,
    string InviteUrl,
    DateTimeOffset ExpiresAt,
    int? MaxUses,
    long Version)
{
    public override string ToString() =>
        $"CreateSpaceInviteResultDto {{ InvitePublicId = {InvitePublicId}, InviteToken = [redacted], "
        + $"InviteUrl = [redacted], ExpiresAt = {ExpiresAt}, MaxUses = {MaxUses}, Version = {Version} }}";
}
