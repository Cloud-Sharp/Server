using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="ISessionStore"/>. 저장된 세션을 기록하고 저장 실패를 주입할 수 있다.
/// 세션 본문은 token hash를 key로 하는 저장 상태(<see cref="PersistedSessions"/>)로 유지하고
/// 조회는 저장 상태와 분리된 복사본을 반환한다. 비밀번호 변경 후처리는 소유자·세션 ID·
/// 이전 보안 버전·만료를 재확인한 뒤에만 현재 세션의 보안 버전을 교체하고, 재확인이 실패하면
/// 현재 세션을 다시 만들지 않고 나머지 세션만 삭제한다.
/// </summary>
public sealed class FakeSessionStore : ISessionStore
{
    private readonly List<UserSession> _storedSessions = new();
    private readonly List<UserSession> _removedSessions = new();
    private readonly List<string> _searchedTokenHashes = new();
    private readonly List<PasswordChangeFinalizationCall> _finalizePasswordChangeCalls = new();
    private readonly Dictionary<string, UserSession> _persistedSessions = new();

    /// <summary>StoreAsync가 반환할 실패 결과. null이면 정상 저장한다.</summary>
    public Result? StoreFailure { get; set; }

    /// <summary>token hash 조회가 반환할 오류. null이면 정상 동작한다.</summary>
    public Error? FindByTokenHashFailure { get; set; }

    /// <summary>세션 삭제가 반환할 실패 결과. null이면 정상 삭제한다. 실패 시 저장 상태를 바꾸지 않는다.</summary>
    public Result? RemoveFailure { get; set; }

    /// <summary>비밀번호 변경 후처리가 반환할 실패 결과. null이면 정상 동작한다. 실패 시 저장 상태를 바꾸지 않는다.</summary>
    public Result? FinalizePasswordChangeFailure { get; set; }

    public IReadOnlyList<UserSession> StoredSessions => _storedSessions;

    /// <summary>RemoveAsync 호출 순서 목록. 성공과 실패 구분 없이 기록한다.</summary>
    public IReadOnlyList<UserSession> RemovedSessions => _removedSessions;

    /// <summary>token hash 조회 호출 순서 목록.</summary>
    public IReadOnlyList<string> SearchedTokenHashes => _searchedTokenHashes;

    /// <summary>비밀번호 변경 후처리 호출 순서 목록.</summary>
    public IReadOnlyList<PasswordChangeFinalizationCall> FinalizePasswordChangeCalls =>
        _finalizePasswordChangeCalls;

    /// <summary>저장 상태 뷰. key는 token hash 값이고 조회가 반환하는 복사본과는 분리되어 있다.</summary>
    public IReadOnlyDictionary<string, UserSession> PersistedSessions => _persistedSessions;

    /// <summary>저장 상태에 세션을 seed한다. 같은 token hash의 세션은 덮어쓴다.</summary>
    public void Seed(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _persistedSessions[session.TokenHash.Value] = Copy(session);
    }

    public Task<Result> StoreAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StoreFailure is not null)
        {
            return Task.FromResult(StoreFailure);
        }

        _storedSessions.Add(session);
        _persistedSessions[session.TokenHash.Value] = Copy(session);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<UserSession?>> FindByTokenHashAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _searchedTokenHashes.Add(tokenHash.Value);
        if (FindByTokenHashFailure is not null)
        {
            return Task.FromResult(Result.Fail<UserSession?>(FindByTokenHashFailure));
        }

        var session = _persistedSessions.TryGetValue(tokenHash.Value, out var stored)
            ? Copy(stored)
            : null;
        return Task.FromResult(Result.Ok(session));
    }

    public Task<Result> RemoveAsync(
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        _removedSessions.Add(session);

        if (RemoveFailure is not null)
        {
            return Task.FromResult(Result.Fail(RemoveFailure.Errors));
        }

        _persistedSessions.Remove(session.TokenHash.Value);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<bool>> FinalizePasswordChangeAsync(
        UserSession currentSession,
        long newSecurityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSession);
        cancellationToken.ThrowIfCancellationRequested();
        _finalizePasswordChangeCalls.Add(new PasswordChangeFinalizationCall(
            currentSession, newSecurityVersion, now));

        if (FinalizePasswordChangeFailure is not null)
        {
            return Task.FromResult(Result.Fail<bool>(FinalizePasswordChangeFailure.Errors));
        }

        var stored = _persistedSessions.GetValueOrDefault(currentSession.TokenHash.Value);
        var eligible = stored is not null
            && stored.UserId == currentSession.UserId
            && stored.SessionId == currentSession.SessionId
            && stored.SecurityVersion == currentSession.SecurityVersion
            && now < stored.IdleExpiresAt
            && now < stored.AbsoluteExpiresAt;

        RemoveOtherSessions(currentSession);

        if (!eligible || stored is null)
        {
            return Task.FromResult(Result.Ok(false));
        }

        _persistedSessions[stored.TokenHash.Value] = Copy(stored, securityVersion: newSecurityVersion);
        return Task.FromResult(Result.Ok(true));
    }

    private void RemoveOtherSessions(UserSession currentSession)
    {
        var staleKeys = _persistedSessions
            .Where(entry => entry.Value.UserId == currentSession.UserId
                && entry.Value.SessionId != currentSession.SessionId)
            .Select(entry => entry.Key)
            .ToList();
        foreach (var key in staleKeys)
        {
            _persistedSessions.Remove(key);
        }
    }

    private static UserSession Copy(UserSession session, long? securityVersion = null) =>
        UserSession.Reconstitute(
            session.SessionId,
            session.UserId,
            session.UserPublicId,
            session.TokenHash,
            securityVersion ?? session.SecurityVersion,
            session.IssuedAt,
            session.LastSeenAt,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt);
}

/// <summary>비밀번호 변경 후처리 호출 기록.</summary>
public sealed record PasswordChangeFinalizationCall(
    UserSession CurrentSession,
    long NewSecurityVersion,
    DateTimeOffset Now);