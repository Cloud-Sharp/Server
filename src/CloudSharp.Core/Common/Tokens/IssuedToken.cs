namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// 토큰 발급 결과. <see cref="PlainToken"/>은 발급 시 호출부에 한 번만 전달하며
/// 저장소에는 <see cref="HashedToken"/>만 보관한다.
/// </summary>
public sealed record IssuedToken(string PlainToken, string HashedToken);