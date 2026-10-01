using System.Globalization;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="ITokenIssuer"/>. 호출마다 결정적 순번을 부여한 평문·hash를 발급하고
/// 발급 이력을 기록해 세션 발급 횟수와 반환된 token을 검증할 수 있다.
/// </summary>
public sealed class FakeTokenIssuer : ITokenIssuer
{
    private int _sequence;
    private readonly List<IssuedToken> _issuedTokens = new();

    public IReadOnlyList<IssuedToken> IssuedTokens => _issuedTokens;

    public IssuedToken Issue(TokenKind kind)
    {
        var sequence = ++_sequence;
        var token = new IssuedToken(
            TokenPrefixes.Get(kind) + "fake-body-" + sequence.ToString("000000", CultureInfo.InvariantCulture),
            "fake-hash-" + sequence.ToString("000000", CultureInfo.InvariantCulture));
        _issuedTokens.Add(token);
        return token;
    }
}