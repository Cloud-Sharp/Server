namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// 토큰 평문과 해시를 한 번에 발급하는 계약. <see cref="ITokenGenerator"/>와
/// <see cref="ITokenHasher"/>를 조합해 호출부가 두 서비스를 직접 다루지 않도록 한다.
/// </summary>
public interface ITokenIssuer
{
    /// <summary>
    /// <paramref name="kind"/>에 대한 평문 토큰과 그 HMAC 해시를 함께 반환한다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/>가 정의되지 않은 enum 값인 경우.
    /// </exception>
    IssuedToken Issue(TokenKind kind);
}