using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Sessions;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.Authentication;

/// <summary>
/// Session token hash를 검증해 최신 User 상태와 SecurityVersion, 만료, PublicId 일치를 확인한다.
/// 활동 시각은 이번 범위에서 갱신하지 않는다.
/// </summary>
public sealed class AuthenticateSessionUseCase(
    IUserRepository userRepository,
    ISessionStore sessionStore,
    ITransactionExecutor transactionExecutor,
    IClock clock)
{
    public async Task<Result<AuthenticatedSessionResult>> ExecuteAsync(
        AuthenticateSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tokenHashResult = TokenHash.Create(query.TokenHash);
        if (tokenHashResult.IsFailed)
        {
            return Result.Fail<AuthenticatedSessionResult>(SessionError.AuthSessionInvalid());
        }

        var tokenHash = tokenHashResult.Value;
        var findResult = await sessionStore.FindByTokenHashAsync(tokenHash, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<AuthenticatedSessionResult>(findResult.Errors);
        }

        var session = findResult.Value;
        if (session is null)
        {
            return Result.Fail<AuthenticatedSessionResult>(SessionError.AuthSessionInvalid());
        }

        var userResult = await transactionExecutor.ExecuteAsync(
            async ct => await LoadAndValidateUserAsync(session, tokenHash, ct),
            cancellationToken: cancellationToken);
        if (userResult.IsFailed)
        {
            return Result.Fail<AuthenticatedSessionResult>(userResult.Errors);
        }

        return Result.Ok(new AuthenticatedSessionResult(
            userResult.Value.Id,
            ToUserDto(userResult.Value),
            session.SessionId));
    }

    private async Task<Result<User>> LoadAndValidateUserAsync(
        UserSession session,
        TokenHash tokenHash,
        CancellationToken cancellationToken)
    {
        var findResult = await userRepository.FindByIdAsync(session.UserId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<User>(findResult.Errors);
        }

        var user = findResult.Value;
        if (user is null)
        {
            return Result.Fail<User>(SessionError.AuthSessionInvalid());
        }

        var validation = session.Validate(
            tokenHash,
            user.SecurityVersion,
            user.Status,
            clock.UtcNow);
        if (validation.IsFailed)
        {
            return Result.Fail<User>(validation.Errors);
        }

        if (session.UserPublicId != user.PublicId)
        {
            return Result.Fail<User>(SessionError.AuthSessionInvalid());
        }

        return Result.Ok(user);
    }

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
