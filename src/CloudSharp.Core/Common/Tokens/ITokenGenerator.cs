namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// 보안 토큰 평문 생성기 계약. 종류별 고정 prefix와 32바이트 CSPRNG 난수를 base64url
/// (padding 없음)로 인코딩한 본문을 결합한 평문 토큰을 반환한다.
/// 비즈니스 로직은 이 계약을 통해서만 평문 토큰을 발급받아야 하며 저장소에는 해시만 저장한다.
/// </summary>
public interface ITokenGenerator
{
    /// <summary>
    /// <paramref name="kind"/>에 해당하는 prefix를 갖는 새 평문 토큰을 반환한다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/>가 정의되지 않은 enum 값인 경우.
    /// </exception>
    string Generate(TokenKind kind);
}