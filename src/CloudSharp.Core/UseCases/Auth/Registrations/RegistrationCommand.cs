using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Auth.Registrations;

/// <summary>
/// 계정 등록 입력. 시스템 역할, 계정 상태, 내부 식별자는 입력으로 받지 않는다.
/// ToString은 비밀번호 원문을 노출하지 않는다.
/// </summary>
public sealed record RegistrationCommand(
    string Email,
    string? UserName,
    string? DisplayName,
    string Password)
{
    public override string ToString() =>
        $"RegistrationCommand {{ Email = {Email}, UserName = {UserName}, "
        + $"DisplayName = {DisplayName}, Password = [redacted] }}";
}

/// <summary>
/// 등록 비밀번호 검증. 8~128자 정책만 적용하며 문자 조합은 강제하지 않고
/// trim·정규화도 수행하지 않는다. 공백만으로 된 값은 거부한다.
/// 이메일·사용자명·표시명 검증은 도메인이 담당한다.
/// </summary>
internal sealed class RegistrationCommandValidator : AbstractValidator<RegistrationCommand>
{
    public RegistrationCommandValidator()
    {
        RuleFor(command => command.Password)
            .NotNull()
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Password is required.")
            .Must(password => !string.IsNullOrWhiteSpace(password))
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Password must not be blank.")
            .Length(8, 128)
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Password length must be between 8 and 128.");
    }
}