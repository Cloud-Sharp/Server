namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// 토큰 평문을 HMAC-SHA-256으로 해시하는 계약. 서버 비밀키는 구현체에 주입되며
/// 호출부는 키를 직접 다루지 않는다. 회전/이전키 동시 검증은 별도 ADR에서 다루며
/// 이 계약의 구현체 교체로 수용한다.
/// </summary>
public interface ITokenHasher
{
    /// <summary>
    /// <paramref name="plainToken"/> 전체 평문의 UTF-8 바이트에 대한 HMAC-SHA-256을
    /// base64url(padding 없음)로 인코딩해 반환한다. prefix를 제거하거나 정규화하지 않는다.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plainToken"/>가 <c>null</c>이거나 빈 문자열인 경우.
    /// </exception>
    string Hash(string plainToken);

    /// <summary>
    /// <paramref name="plainToken"/>의 해시가 <paramref name="expectedHashedToken"/>와
    /// 일치하는지 constant-time 비교로 검증한다. 저장소에 보관된 해시와 발급 시 받은 평문을
    /// 비교할 때 반드시 이 메서드를 사용해야 타이밍 공격을 회피할 수 있다.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plainToken"/>가 <c>null</c>이거나 빈 문자열인 경우.
    /// </exception>
    bool Verify(string plainToken, string expectedHashedToken);
}