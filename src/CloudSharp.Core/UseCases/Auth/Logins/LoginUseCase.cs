using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
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
    ISessionStore sessionStore,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    ITransactionExecutor transactionExecutor,
    IClock clock)
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

        return await IssueSessionAsync(user, cancellationToken);
    }

    private async Task<Result<LoginResultDto>> IssueSessionAsync(
        User user,
        CancellationToken cancellationToken)
    {
        if (user.Id <= 0)
        {
            throw new InvalidOperationException(
                "UserRepository returned a user without a positive internal id.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var issuedToken = tokenIssuer.Issue(TokenKind.Session);

        var tokenHashResult = TokenHash.Create(issuedToken.HashedToken);
        if (tokenHashResult.IsFailed)
        {
            return Result.Fail<LoginResultDto>(tokenHashResult.Errors);
        }

        var session = UserSession.Issue(
            user.Id,
            user.PublicId,
            tokenHashResult.Value,
            user.SecurityVersion,
            clock.UtcNow);

        var storeResult = await sessionStore.StoreAsync(session, cancellationToken);
        if (storeResult.IsFailed)
        {
            return Result.Fail<LoginResultDto>(storeResult.Errors);
        }

        return Result.Ok(new LoginResultDto(
            issuedToken.PlainToken,
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