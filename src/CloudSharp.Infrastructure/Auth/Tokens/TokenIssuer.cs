using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// <see cref="ITokenGenerator"/>와 <see cref="ITokenHasher"/>를 조합한 <see cref="ITokenIssuer"/> 구현.
/// </summary>
public sealed class TokenIssuer : ITokenIssuer
{
    private readonly ITokenGenerator _generator;
    private readonly ITokenHasher _hasher;

    public TokenIssuer(ITokenGenerator generator, ITokenHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(hasher);
        _generator = generator;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public IssuedToken Issue(TokenKind kind)
    {
        var plain = _generator.Generate(kind);
        var hashed = _hasher.Hash(plain);
        return new IssuedToken(plain, hashed);
    }
}