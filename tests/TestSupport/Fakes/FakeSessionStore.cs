using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Domain.Sessions;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="ISessionStore"/>. 저장된 세션을 기록하고 저장 실패를 주입할 수 있다.
/// </summary>
public sealed class FakeSessionStore : ISessionStore
{
    private readonly List<UserSession> _storedSessions = new();

    /// <summary>StoreAsync가 반환할 실패 결과. null이면 정상 저장한다.</summary>
    public Result? StoreFailure { get; set; }

    public IReadOnlyList<UserSession> StoredSessions => _storedSessions;

    public Task<Result> StoreAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StoreFailure is not null)
        {
            return Task.FromResult(StoreFailure);
        }

        _storedSessions.Add(session);
        return Task.FromResult(Result.Ok());
    }
}