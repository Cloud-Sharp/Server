using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Users;

/// <summary>
/// 사용자명 값 객체. trim한 표시값과 <see cref="string.ToUpperInvariant"/> 비교값을 함께 보유한다.
/// DB uniqueness와 로그인 비교는 <see cref="Normalized"/>를 기준으로 한다.
/// </summary>
public sealed record NormalizedName
{
    internal const int MaxLength = 30;

    /// <summary>
    /// 표시용 사용자명. trim 처리된 원본 문자열이다.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 비교·uniqueness용 정규화 사용자명. <see cref="Value"/>를 trim하고 <see cref="string.ToUpperInvariant"/>한 값이다.
    /// </summary>
    public string Normalized { get; }

    private NormalizedName(string value, string normalized)
    {
        Value = value;
        Normalized = normalized;
    }

    /// <summary>
    /// 외부 입력을 <see cref="NormalizedName"/>로 생성한다. 빈 값, 30자 초과를 거부한다.
    /// </summary>
    public static Result<NormalizedName> Create(string? userName)
    {
        if (userName is null)
        {
            return Result.Fail<NormalizedName>(new Error("User name is required.")
                .WithMetadata("ErrorCode", ErrorCodes.User.NameInvalid)
                .WithMetadata("PropertyName", "UserName"));
        }

        var validator = new NormalizedNameValidator();
        var validationResult = validator.Validate(userName);
        if (!validationResult.IsValid)
        {
            return validationResult.ToFailureResult<NormalizedName>();
        }

        var trimmed = userName.Trim();
        var normalized = trimmed.ToUpperInvariant();
        return Result.Ok(new NormalizedName(trimmed, normalized));
    }

    /// <summary>
    /// Infrastructure 복원 전용. 신규 validation은 수행하지 않고 방어 검증만 수행한다.
    /// </summary>
    public static NormalizedName Reconstitute(string value, string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("User name value must not be empty.", nameof(value));
        }

        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"User name length must not exceed {MaxLength}.", nameof(value));
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Normalized user name must not be empty.", nameof(normalized));
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"Normalized user name length must not exceed {MaxLength}.", nameof(normalized));
        }

        if (!normalized.Equals(value.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Normalized user name must match the uppercased trimmed value.", nameof(normalized));
        }

        return new NormalizedName(value.Trim(), normalized);
    }
}

internal sealed class NormalizedNameValidator : AbstractValidator<string>
{
    public NormalizedNameValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.User.NameInvalid)
            .WithMessage("User name is required.")
            .Must(v => v is null || v.Trim().Length <= NormalizedName.MaxLength)
            .WithErrorCode(ErrorCodes.User.NameInvalid)
            .WithMessage($"User name length must not exceed {NormalizedName.MaxLength}.");
    }
}