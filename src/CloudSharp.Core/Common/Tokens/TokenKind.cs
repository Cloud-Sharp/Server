namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// 서버 전역에서 발급하는 보안 토큰의 종류. 각 종류는 고유 prefix를 가지며 저장소에는
/// HMAC-SHA-256 해시만 저장된다. 평문은 발급 시 한 번만 호출부에 반환한다.
/// </summary>
public enum TokenKind
{
    /// <summary>
    /// 사용자 세션 토큰. prefix <c>cs_sess_</c>.
    /// </summary>
    Session,

    /// <summary>
    /// MCP 자격 증명 토큰. prefix <c>cs_mcp_</c>.
    /// </summary>
    McpCredential,

    /// <summary>
    /// 공유 링크 토큰. prefix <c>cs_share_</c>.
    /// </summary>
    ShareLink,

    /// <summary>
    /// 스페이스 초대 토큰. prefix <c>cs_inv_</c>.
    /// </summary>
    SpaceInvite,

    /// <summary>
    /// 단명 다운로드 허가 토큰. prefix <c>cs_dl_</c>.
    /// </summary>
    DownloadGrant,
}