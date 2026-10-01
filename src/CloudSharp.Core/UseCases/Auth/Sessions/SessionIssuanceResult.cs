namespace CloudSharp.Core.UseCases.Auth.Sessions;

/// <summary>
/// 세션 저장 완료 후 전달하는 평문 토큰과 만료 시각. ToString은 평문 토큰을 노출하지 않는다.
/// </summary>
public sealed record SessionIssuanceResult(
    string AccessToken,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt)
{
    public override string ToString() =>
        $"SessionIssuanceResult {{ AccessToken = [redacted], "
        + $"IdleExpiresAt = {IdleExpiresAt}, AbsoluteExpiresAt = {AbsoluteExpiresAt} }}";
}
