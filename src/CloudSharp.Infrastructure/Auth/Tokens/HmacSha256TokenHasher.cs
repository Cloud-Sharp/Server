using System.Security.Cryptography;
using System.Text;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// <see cref="ITokenHasher"/>의 프로덕션 구현. <see cref="TokenHashingOptions.ActiveKey"/>로
/// HMAC-SHA-256 서명을 수행하고 32바이트 결과를 padding 없는 base64url로 반환한다.
/// <see cref="Verify"/>는 <see cref="CryptographicOperations.FixedTimeEquals"/>로 constant-time
/// 비교를 수행한다.
/// </summary>
public sealed class HmacSha256TokenHasher : ITokenHasher
{
    private readonly byte[] _key;

    public HmacSha256TokenHasher(TokenHashingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ActiveKey))
        {
            throw new ArgumentException("TokenHashing:ActiveKey is missing.", nameof(options));
        }
        try
        {
            _key = Convert.FromBase64String(options.ActiveKey.Trim());
        }
        catch (FormatException)
        {
            throw new ArgumentException("TokenHashing:ActiveKey is not valid Base64.", nameof(options));
        }
        if (_key.Length < TokenHashingOptionsValidator.MinimumKeyBytes)
        {
            throw new ArgumentException(
                $"TokenHashing:ActiveKey must be at least {TokenHashingOptionsValidator.MinimumKeyBytes} bytes after Base64 decoding.",
                nameof(options));
        }
    }

    /// <inheritdoc />
    public string Hash(string plainToken)
    {
        ArgumentNullException.ThrowIfNull(plainToken);
        if (plainToken.Length == 0)
        {
            throw new ArgumentException("Plain token must not be empty.", nameof(plainToken));
        }
        var bytes = Encoding.UTF8.GetBytes(plainToken);
        var hash = HMACSHA256.HashData(_key, bytes);
        return Base64Url.Encode(hash);
    }

    /// <inheritdoc />
    public bool Verify(string plainToken, string expectedHashedToken)
    {
        ArgumentNullException.ThrowIfNull(plainToken);
        if (plainToken.Length == 0)
        {
            throw new ArgumentException("Plain token must not be empty.", nameof(plainToken));
        }
        if (string.IsNullOrEmpty(expectedHashedToken))
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Base64Url.Decode(expectedHashedToken);
        }
        catch (FormatException)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(plainToken);
        var actual = HMACSHA256.HashData(_key, bytes);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}