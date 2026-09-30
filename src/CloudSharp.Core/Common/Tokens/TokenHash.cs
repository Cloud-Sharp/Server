using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Common.Tokens;

/// <summary>
/// Redis에 저장하는 세션 토큰의 HMAC-SHA-256 해시 값을 캡슐화하는 값 객체.
/// 원문 토큰, 평문 접두사 <c>cs_</c>, 빈 값, 128자 초과 값을 거부한다.
/// <see cref="ToString"/>은 항상 redacted 표현을 반환해 로그와 예외에서 해시가 노출되지 않게 한다.
/// </summary>
public sealed record TokenHash
{
    internal const string Redacted = "[redacted]";
    internal const string RawTokenPrefix = "cs_";
    internal const int MaxLength = 128;

    /// <summary>
    /// 해시 값. base64url 인코딩된 HMAC-SHA-256 결과물이다.
    /// </summary>
    public string Value { get; }

    private TokenHash(string value)
    {
        Value = value;
    }

    /// <summary>
    /// 외부 입력이나 저장소에서 읽은 해시 값을 <see cref="TokenHash"/>로 생성한다.
    /// 빈 값, 128자 초과, <c>cs_</c> 평문 토큰 형태를 거부한다.
    /// </summary>
    public static Result<TokenHash> Create(string? value)
    {
        if (value is null)
        {
            return Result.Fail<TokenHash>(new Error("Token hash is required.")
                .WithMetadata("ErrorCode", ErrorCodes.Session.TokenHashInvalid)
                .WithMetadata("PropertyName", "TokenHash"));
        }

        var validator = new TokenHashValidator();
        var validationResult = validator.Validate(value);
        if (!validationResult.IsValid)
        {
            return validationResult.ToFailureResult<TokenHash>();
        }

        return Result.Ok(new TokenHash(value.Trim()));
    }

    /// <summary>
    /// Infrastructure 복원 전용. 신규 validation을 수행하지 않고 방어 검증만 수행한다.
    /// </summary>
    public static TokenHash Reconstitute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Token hash must not be empty.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"Token hash length must not exceed {MaxLength}.", nameof(value));
        }

        if (trimmed.StartsWith(RawTokenPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Token hash must not be a raw token.", nameof(value));
        }

        return new TokenHash(trimmed);
    }

    /// <summary>
    /// 항상 redacted 값을 반환한다. 로그, 예외 메시지, trace에서 해시가 노출되지 않게 한다.
    /// </summary>
    public override string ToString() => Redacted;
}

internal sealed class TokenHashValidator : AbstractValidator<string>
{
    public TokenHashValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.Session.TokenHashInvalid)
            .WithMessage("Token hash is required.")
            .Must((_, value) => !value.Trim().StartsWith(TokenHash.RawTokenPrefix, StringComparison.Ordinal))
            .WithErrorCode(ErrorCodes.Session.TokenHashInvalid)
            .WithMessage("Token hash must not be a raw token.")
            .Must((_, value) => value.Trim().Length <= TokenHash.MaxLength)
            .WithErrorCode(ErrorCodes.Session.TokenHashInvalid)
            .WithMessage($"Token hash length must not exceed {TokenHash.MaxLength}.");
    }
}