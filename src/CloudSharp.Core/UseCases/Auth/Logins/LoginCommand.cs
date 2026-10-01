using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Auth.Logins;

/// <summary>
/// 로그인 입력. LoginId는 이메일 주소만 지원하며 시스템 역할, 계정 상태, 내부 식별자는 입력으로 받지 않는다.
/// ToString은 비밀번호 원문을 노출하지 않는다.
/// </summary>
public sealed record LoginCommand(string LoginId, string Password)
{
    public override string ToString() =>
        $"LoginCommand {{ LoginId = {LoginId}, Password = [redacted] }}";
}

/// <summary>
/// 로그인 비밀번호 구조 검증. 등록 정책의 최소 길이를 재적용하지 않고
/// null, 공백만으로 된 값, 128자 초과만 거부하며 trim·정규화도 수행하지 않는다.
/// 검증 실패는 <see cref="ErrorCodes.Session.AuthInvalidCredentials"/> 하나로 수렴되므로
/// 규칙별 세부 메시지는 외부에 노출되지 않는다.
/// 이메일 검증은 <see cref="Domain.Users.EmailAddress"/>가 담당한다.
/// </summary>
internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Password)
            .NotNull()
            .WithErrorCode(ErrorCodes.Session.AuthInvalidCredentials)
            .WithMessage("Password is required.")
            .Must(password => !string.IsNullOrWhiteSpace(password))
            .WithErrorCode(ErrorCodes.Session.AuthInvalidCredentials)
            .WithMessage("Password must not be blank.")
            .MaximumLength(128)
            .WithErrorCode(ErrorCodes.Session.AuthInvalidCredentials)
            .WithMessage("Password length must not exceed 128.");
    }
}