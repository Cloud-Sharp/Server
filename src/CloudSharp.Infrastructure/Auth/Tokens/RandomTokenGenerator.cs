using System.Security.Cryptography;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// <see cref="ITokenGenerator"/>의 프로덕션 구현. 운영체제 CSPRNG로 32바이트 난수를 생성해
/// padding 없는 base64url로 인코딩하고 종류별 고정 prefix를 결합한다.
/// </summary>
public sealed class RandomTokenGenerator : ITokenGenerator
{
    private const int RandomBytes = 32;

    /// <inheritdoc />
    public string Generate(TokenKind kind)
    {
        // TokenPrefixes.Get throws ArgumentOutOfRangeException for undefined enum values.
        var prefix = TokenPrefixes.Get(kind);
        var bytes = RandomNumberGenerator.GetBytes(RandomBytes);
        return prefix + Base64Url.Encode(bytes);
    }
}