namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// 토큰 HMAC 서명에 사용할 활성 비밀키 설정. <c>TokenHashing:ActiveKey</c> 설정 키에
/// Base64로 인코딩된 최소 32바이트 키를 주입한다. 실제 키 값은 source control,
/// appsettings, 로그, 오류 메시지에 기록하지 않고 배포 secret으로만 주입한다.
/// </summary>
public sealed class TokenHashingOptions
{
    public const string SectionName = "TokenHashing";

    /// <summary>
    /// Base64로 인코딩된 HMAC-SHA-256 비밀키. 디코딩 시 최소 32바이트여야 한다.
    /// </summary>
    public string ActiveKey { get; set; } = string.Empty;
}