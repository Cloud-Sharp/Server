namespace CloudSharp.Core.UseCases.Auth.Dtos;

/// <summary>
/// 로그인 성공 결과. AccessToken 평문은 이 DTO로 호출부에 한 번만 전달되며
/// 저장소와 로그에는 절대 남지 않는다. ToString은 평문 token을 노출하지 않는다.
/// </summary>
public sealed record LoginResultDto(
    string AccessToken,
    string TokenType,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    UserDto User)
{
    public override string ToString() =>
        $"LoginResultDto {{ AccessToken = [redacted], TokenType = {TokenType}, "
        + $"IdleExpiresAt = {IdleExpiresAt}, AbsoluteExpiresAt = {AbsoluteExpiresAt}, User = {User} }}";
}