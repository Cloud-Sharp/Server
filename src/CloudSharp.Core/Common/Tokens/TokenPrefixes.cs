namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// <see cref="TokenKind"/>별 발급 평문 토큰의 prefix를 제공한다. 모든 토큰은 <c>cs_</c>로 시작하는
/// 고정 prefix 뒤에 32바이트 CSPRNG 난수의 base64url(padding 없음) 본문이 결합된다.
/// </summary>
public static class TokenPrefixes
{
    /// <summary>
    /// <see cref="TokenKind"/>에 해당하는 prefix를 반환한다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/>가 정의되지 않은 enum 값인 경우.
    /// </exception>
    public static string Get(TokenKind kind) => kind switch
    {
        TokenKind.Session => "cs_sess_",
        TokenKind.McpCredential => "cs_mcp_",
        TokenKind.ShareLink => "cs_share_",
        TokenKind.SpaceInvite => "cs_inv_",
        TokenKind.DownloadGrant => "cs_dl_",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported token kind."),
    };
}