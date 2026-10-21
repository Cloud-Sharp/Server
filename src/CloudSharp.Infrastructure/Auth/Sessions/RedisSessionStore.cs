using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using FluentResults;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// <see cref="ISessionStore"/>의 Redis 구현. 세션 본문과 사용자별 인덱스를
/// <see cref="SessionStoreLuaScripts"/>로 원자적으로 변경하고 조회는 저장 상태의 독립 복원본을 반환한다.
/// 취소는 명령 발송 전에만 관찰하고 발송한 명령은 Redis 명령 timeout(기본 5초)까지 기다린다.
/// 알려진 연결·timeout 장애만 <c>DEPENDENCY_UNAVAILABLE</c>로 변환하고 서버 오류는 전파하며,
/// 손상된 payload는 세션 없음으로 처리하고 본문을 정리한다.
/// </summary>
internal sealed class RedisSessionStore(IConnectionMultiplexer connection) : ISessionStore
{
    private const long StoreRejected = -1;
    private const long Finalized = 1;

    public async Task<Result> StoreAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = connection.GetDatabase();
            var result = await database.ScriptEvaluateAsync(
                SessionStoreLuaScripts.Store,
                [SessionRedisPayload.BodyKey(session.TokenHash), SessionRedisPayload.UserIndexKey(session.UserId)],
                SessionRedisPayload.ToStoreArguments(session));
            if ((long)result == StoreRejected)
            {
                throw new InvalidOperationException(
                    "A session body already exists for the issued token hash; the session store never overwrites it.");
            }

            return Result.Ok();
        }
        catch (Exception exception) when (RedisAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail(CommonError.DependencyUnavailable());
        }
    }

    public async Task<Result<UserSession?>> FindByTokenHashAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        cancellationToken.ThrowIfCancellationRequested();

        var database = connection.GetDatabase();
        try
        {
            var entries = await database.HashGetAllAsync(SessionRedisPayload.BodyKey(tokenHash));
            if (entries.Length == 0)
            {
                return Result.Ok<UserSession?>(null);
            }

            return Result.Ok<UserSession?>(SessionRedisPayload.ToDomain(entries, tokenHash));
        }
        catch (SessionPayloadCorruptException)
        {
            await TryDeleteCorruptSessionAsync(database, tokenHash);
            return Result.Ok<UserSession?>(null);
        }
        catch (Exception exception) when (RedisAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<UserSession?>(CommonError.DependencyUnavailable());
        }
    }

    private static async Task TryDeleteCorruptSessionAsync(IDatabase database, TokenHash tokenHash)
    {
        try
        {
            await database.KeyDeleteAsync(SessionRedisPayload.BodyKey(tokenHash));
        }
        catch (Exception exception) when (RedisAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            // The session is already treated as invalid; cleanup is best effort.
        }
    }

    public async Task<Result> RemoveAsync(
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = connection.GetDatabase();
            await database.ScriptEvaluateAsync(
                SessionStoreLuaScripts.Remove,
                [SessionRedisPayload.BodyKey(session.TokenHash), SessionRedisPayload.UserIndexKey(session.UserId)],
                SessionRedisPayload.ToRemoveArguments(session));
            return Result.Ok();
        }
        catch (Exception exception) when (RedisAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail(CommonError.DependencyUnavailable());
        }
    }

    public async Task<Result<bool>> FinalizePasswordChangeAsync(
        UserSession currentSession,
        long newSecurityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSession);
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("FinalizePasswordChange requires UTC now.", nameof(now));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = connection.GetDatabase();
            var result = await database.ScriptEvaluateAsync(
                SessionStoreLuaScripts.FinalizePasswordChange,
                [SessionRedisPayload.BodyKey(currentSession.TokenHash), SessionRedisPayload.UserIndexKey(currentSession.UserId)],
                SessionRedisPayload.ToFinalizeArguments(currentSession, newSecurityVersion, now));
            return Result.Ok((long)result == Finalized);
        }
        catch (Exception exception) when (RedisAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<bool>(CommonError.DependencyUnavailable());
        }
    }
}