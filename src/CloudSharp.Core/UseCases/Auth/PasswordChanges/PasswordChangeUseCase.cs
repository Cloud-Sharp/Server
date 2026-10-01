using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.PasswordChanges;

/// <summary>
/// 비밀번호 변경 use case. 입력 검증 → 세션 조회 → 트랜잭션 안 User 조회·세션 검증·버전 확인·
/// 현재 비밀번호 확인·비밀번호 교체·저장 → commit 후 현재 세션 보존·나머지 세션 폐기 순서로 실행한다.
/// commit 후 Redis 후처리가 실패하면 이미 변경된 비밀번호를 유지하고 모든 세션을 무효화한 뒤
/// <c>DEPENDENCY_UNAVAILABLE</c>을 반환한다(재로그인으로 복구). commit 후 보안 정리는
/// 요청 취소와 무관하게 별도 10초 deadline 안에서만 동작한다.
/// </summary>
public sealed class PasswordChangeUseCase(
    IUserRepository userRepository,
    ISessionStore sessionStore,
    IPasswordHasher passwordHasher,
    ITransactionExecutor transactionExecutor,
    IClock clock)
{
    /// <summary>commit 후 보안 정리의 최대 소요 시간.</summary>
    private static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(10);

    /// <summary>compensation 재시도 최대 횟수. concurrency 충돌에만 fresh 조회로 재시도한다.</summary>
    private const int MaxCompensationAttempts = 3;

    public async Task<Result<PasswordChangeResultDto>> ExecuteAsync(
        PasswordChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new PasswordChangeCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<PasswordChangeResultDto>();
        }

        var tokenHashResult = TokenHash.Create(command.TokenHash);
        if (tokenHashResult.IsFailed)
        {
            return Result.Fail<PasswordChangeResultDto>(tokenHashResult.Errors);
        }

        var findResult = await sessionStore.FindByTokenHashAsync(
            tokenHashResult.Value, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<PasswordChangeResultDto>(findResult.Errors);
        }

        var session = findResult.Value;
        if (session is null
            || session.UserId != command.UserId
            || session.SessionId != command.SessionId)
        {
            return Result.Fail<PasswordChangeResultDto>(SessionError.AuthSessionInvalid());
        }

        var changeResult = await transactionExecutor.ExecuteAsync(
            async ct => await ChangePasswordInTransactionAsync(command, tokenHashResult.Value, session, ct),
            cancellationToken: cancellationToken);
        if (changeResult.IsFailed)
        {
            return Result.Fail<PasswordChangeResultDto>(changeResult.Errors);
        }

        var changedUser = changeResult.Value;
        using var cleanupSource = new CancellationTokenSource(CleanupDeadline);
        try
        {
            var finalized = await sessionStore.FinalizePasswordChangeAsync(
                session,
                changedUser.SecurityVersion,
                clock.UtcNow,
                cleanupSource.Token);
            if (finalized.IsSuccess)
            {
                return Result.Ok(new PasswordChangeResultDto(changedUser.Version));
            }

            return await InvalidateSessionsAfterFinalizationFailureAsync(session, changedUser, cleanupSource.Token);
        }
        catch (OperationCanceledException) when (cleanupSource.Token.IsCancellationRequested)
        {
            return Result.Fail<PasswordChangeResultDto>(CommonError.DependencyUnavailable());
        }
    }

    private async Task<Result<User>> ChangePasswordInTransactionAsync(
        PasswordChangeCommand command,
        TokenHash tokenHash,
        UserSession session,
        CancellationToken cancellationToken)
    {
        var findResult = await userRepository.FindByIdAsync(command.UserId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<User>(findResult.Errors);
        }

        var user = findResult.Value;
        if (user is null)
        {
            return Result.Fail<User>(SessionError.AuthSessionInvalid());
        }

        var sessionValidation = session.Validate(
            tokenHash, user.SecurityVersion, user.Status, clock.UtcNow);
        if (sessionValidation.IsFailed)
        {
            return Result.Fail<User>(sessionValidation.Errors);
        }

        if (user.Version != command.ExpectedVersion)
        {
            return Result.Fail<User>(CommonError.PreconditionFailed());
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword))
        {
            return Result.Fail<User>(UserError.PasswordMismatch());
        }

        var change = user.ChangePasswordHash(
            passwordHasher.Hash(command.NewPassword), clock.UtcNow);
        if (change.IsFailed)
        {
            return Result.Fail<User>(change.Errors);
        }

        var saveResult = await userRepository.SaveAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<User>(saveResult.Errors);
        }

        return saveResult;
    }

    /// <summary>
    /// commit 후 정리 실패 시 이미 변경된 비밀번호를 유지한 채 모든 세션을 무효화한다.
    /// 현재 세션 삭제에 성공하면 나머지 세션은 저장소의 보안 버전 불일치로 이미 무효하다.
    /// 삭제도 실패하거나 결과를 확정할 수 없으면 별도 트랜잭션에서 보안 버전을 한 번 더
    /// 증가시켜 현재 세션까지 확정 무효화한다. 어느 경우든 <c>DEPENDENCY_UNAVAILABLE</c>을 반환한다.
    /// </summary>
    private async Task<Result<PasswordChangeResultDto>> InvalidateSessionsAfterFinalizationFailureAsync(
        UserSession session,
        User changedUser,
        CancellationToken cleanupToken)
    {
        var removed = await sessionStore.RemoveAsync(session, cleanupToken);
        if (removed.IsSuccess)
        {
            return Result.Fail<PasswordChangeResultDto>(CommonError.DependencyUnavailable());
        }

        await CompensateSecurityVersionAsync(changedUser, cleanupToken);
        return Result.Fail<PasswordChangeResultDto>(CommonError.DependencyUnavailable());
    }

    private async Task CompensateSecurityVersionAsync(User changedUser, CancellationToken cleanupToken)
    {
        for (var attempt = 1; attempt <= MaxCompensationAttempts; attempt++)
        {
            var result = await transactionExecutor.ExecuteAsync(
                async ct => await RevokeSessionsIfVersionUnchangedAsync(changedUser, ct),
                cancellationToken: cleanupToken);
            if (result.IsSuccess)
            {
                return;
            }

            if (IsPreconditionFailed(result) && attempt < MaxCompensationAttempts)
            {
                continue;
            }

            return;
        }
    }

    private async Task<Result> RevokeSessionsIfVersionUnchangedAsync(
        User changedUser,
        CancellationToken cancellationToken)
    {
        var findResult = await userRepository.FindByIdAsync(changedUser.Id, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail(findResult.Errors);
        }

        var current = findResult.Value;
        if (current is null || current.SecurityVersion != changedUser.SecurityVersion)
        {
            return Result.Ok();
        }

        var revoke = current.RevokeAllSessions(clock.UtcNow);
        if (revoke.IsFailed)
        {
            return Result.Fail(revoke.Errors);
        }

        var saveResult = await userRepository.SaveAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail(saveResult.Errors);
        }

        return Result.Ok();
    }

    private static bool IsPreconditionFailed(Result result) =>
        result.Errors.Any(error =>
            error.Metadata is not null
            && string.Equals(
                (string?)error.Metadata[CloudSharpError.ErrorCodeMetadataKey],
                ErrorCodes.Common.PreconditionFailed,
                StringComparison.Ordinal));
}