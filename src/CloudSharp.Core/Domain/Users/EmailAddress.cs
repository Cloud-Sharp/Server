using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Users;

/// <summary>
/// 이메일 값 객체. trim한 표시값과 <see cref="string.ToUpperInvariant"/> 비교값을 함께 보유한다.
/// DB uniqueness와 로그인 비교는 <see cref="Normalized"/>를 기준으로 한다.
/// </summary>
public sealed record EmailAddress
{
    internal const int MaxLength = 320;

    /// <summary>
    /// 표시용 이메일. trim 처리된 원본 문자열이다.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 비교·uniqueness용 정규화 이메일. <see cref="Value"/>를 trim하고 <see cref="string.ToUpperInvariant"/>한 값이다.
    /// </summary>
    public string Normalized { get; }

    private EmailAddress(string value, string normalized)
    {
        Value = value;
        Normalized = normalized;
    }

    /// <summary>
    /// 외부 입력을 <see cref="EmailAddress"/>로 생성한다. 빈 값, 320자 초과, 잘못된 형식을 거부한다.
    /// </summary>
    public static Result<EmailAddress> Create(string? email)
    {
        if (email is null)
        {
            return Result.Fail<EmailAddress>(new Error("Email is required.")
                .WithMetadata("ErrorCode", ErrorCodes.User.InvalidEmail)
                .WithMetadata("PropertyName", "Email"));
        }

        var validator = new EmailAddressValidator();
        var validationResult = validator.Validate(email);
        if (!validationResult.IsValid)
        {
            return validationResult.ToFailureResult<EmailAddress>();
        }

        var trimmed = email.Trim();
        var normalized = trimmed.ToUpperInvariant();
        return Result.Ok(new EmailAddress(trimmed, normalized));
    }

    /// <summary>
    /// Infrastructure 복원 전용. 신규 validation은 수행하지 않고 방어 검증만 수행한다.
    /// </summary>
    public static EmailAddress Reconstitute(string value, string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Email value must not be empty.", nameof(value));
        }

        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"Email length must not exceed {MaxLength}.", nameof(value));
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Normalized email must not be empty.", nameof(normalized));
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"Normalized email length must not exceed {MaxLength}.", nameof(normalized));
        }

        if (!normalized.Equals(value.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Normalized email must match the uppercased trimmed value.", nameof(normalized));
        }

        return new EmailAddress(value.Trim(), normalized);
    }
}

internal sealed class EmailAddressValidator : AbstractValidator<string>
{
    public EmailAddressValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.User.InvalidEmail)
            .WithMessage("Email is required.")
            .Must(v => v is null || v.Trim().Length <= EmailAddress.MaxLength)
            .WithErrorCode(ErrorCodes.User.InvalidEmail)
            .WithMessage($"Email length must not exceed {EmailAddress.MaxLength}.")
            .Must(v => v is null || v.Trim().Contains('@', StringComparison.Ordinal))
            .WithErrorCode(ErrorCodes.User.InvalidEmail)
            .WithMessage("Email format is invalid.");
    }
}