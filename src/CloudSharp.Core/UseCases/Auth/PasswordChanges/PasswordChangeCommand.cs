using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Auth.PasswordChanges;

/// <summary>
/// 비밀번호 변경 입력. 소유자·세션 식별 정보는 endpoint가 인증된 Session context에서
/// 채워야 하며 클라이언트가 임의로 지정할 수 없다. ToString은 비밀번호와 token hash를 노출하지 않는다.
/// </summary>
public sealed record PasswordChangeCommand(
    long UserId,
    Guid SessionId,
    string TokenHash,
    long ExpectedVersion,
    string CurrentPassword,
    string NewPassword)
{
    public override string ToString() =>
        $"PasswordChangeCommand {{ UserId = {UserId}, SessionId = {SessionId}, "
        + $"TokenHash = [redacted], ExpectedVersion = {ExpectedVersion}, "
        + "CurrentPassword = [redacted], NewPassword = [redacted] }}";
}

/// <summary>
/// 비밀번호 변경 입력 검증. 새 비밀번호는 등록 정책(8~128자, 공백만 된 값 거부)을 적용하고
/// 현재 비밀번호는 로그인의 구조 검증(null, 공백만 된 값, 128자 초과 거부)을 적용한다.
/// 어느 쪽도 trim·정규화하지 않으며 새 비밀번호가 현재 비밀번호와 같은 것은 허용한다.
/// token hash 검증은 <see cref="Common.Tokens.TokenHash"/>가 담당한다.
/// </summary>
internal sealed class PasswordChangeCommandValidator : AbstractValidator<PasswordChangeCommand>
{
    public PasswordChangeCommandValidator()
    {
        RuleFor(command => command.CurrentPassword)
            .NotNull()
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Current password is required.")
            .Must(password => !string.IsNullOrWhiteSpace(password))
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Current password must not be blank.")
            .MaximumLength(128)
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Current password length must not exceed 128.");

        RuleFor(command => command.NewPassword)
            .NotNull()
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("New password is required.")
            .Must(password => !string.IsNullOrWhiteSpace(password))
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("New password must not be blank.")
            .Length(8, 128)
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("New password length must be between 8 and 128.");
    }
}