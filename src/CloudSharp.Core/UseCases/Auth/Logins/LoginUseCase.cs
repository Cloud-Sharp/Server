using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Sessions;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.Logins;

/// <summary>
/// 이메일 로그인 use case. 입력 검증 → 트랜잭션 안 정규화 이메일 조회 →
/// 트랜잭션 밖 CanLogin·비밀번호 확인 → 세션 발급·저장 → 결과 반환 순서로 실행한다.
/// 계정 없음, 정지·삭제 계정, 비밀번호 불일치, 잘못된 입력을 모두 동일한
/// <c>AUTH_INVALID_CREDENTIALS</c> 오류로 반환해 계정 존재 여부와 상태를 노출하지 않는다.
/// </summary>
public sealed class LoginUseCase(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    SessionIssuanceService sessionIssuanceService,
    ITransactionExecutor transactionExecutor)
{
    private const string BearerTokenType = "Bearer";

    public async Task<Result<LoginResultDto>> ExecuteAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailResult = EmailAddress.Create(command.LoginId);
        if (emailResult.IsFailed)
        {
            return InvalidCredentials();
        }

        var validation = new LoginCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return InvalidCredentials();
        }

        var lookupResult = await transactionExecutor.ExecuteAsync(
            async ct => await userRepository.FindByNormalizedEmailAsync(emailResult.Value.Normalized, ct),
            cancellationToken: cancellationToken);
        if (lookupResult.IsFailed)
        {
            return Result.Fail<LoginResultDto>(lookupResult.Errors);
        }

        var user = lookupResult.Value;
        if (user is null || !user.CanLogin() || !passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return InvalidCredentials();
        }

        var sessionResult = await sessionIssuanceService.IssueAsync(user, cancellationToken);
        if (sessionResult.IsFailed)
        {
            return Result.Fail<LoginResultDto>(sessionResult.Errors);
        }

        var session = sessionResult.Value;
        return Result.Ok(new LoginResultDto(
            session.AccessToken,
            BearerTokenType,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            ToUserDto(user)));
    }

    private static Result<LoginResultDto> InvalidCredentials() =>
        Result.Fail<LoginResultDto>(SessionError.AuthInvalidCredentials());

    private static UserDto ToUserDto(User user) =>
        new(
            user.PublicId,
            user.Email.Value,
            user.UserName?.Value,
            user.DisplayName,
            user.SystemRole,
            user.Status,
            user.CreatedAt,
            user.UpdatedAt);
}
