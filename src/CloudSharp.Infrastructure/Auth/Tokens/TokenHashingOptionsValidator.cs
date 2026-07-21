using Microsoft.Extensions.Options;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// <see cref="TokenHashingOptions"/>의 시작 시점 검증. 비밀키가 누락되었거나
/// Base64로 디코딩할 수 없거나 32바이트 미만이면 호스트 시작을 거부한다.
/// 오류 메시지에는 키 원문을 절대 포함하지 않는다.
/// </summary>
public sealed class TokenHashingOptionsValidator : IValidateOptions<TokenHashingOptions>
{
    public const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, TokenHashingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ActiveKey))
        {
            return ValidateOptionsResult.Fail("TokenHashing:ActiveKey is missing or empty.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(options.ActiveKey.Trim());
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("TokenHashing:ActiveKey is not valid Base64.");
        }

        if (keyBytes.Length < MinimumKeyBytes)
        {
            return ValidateOptionsResult.Fail($"TokenHashing:ActiveKey must be at least {MinimumKeyBytes} bytes after Base64 decoding.");
        }

        return ValidateOptionsResult.Success;
    }
}