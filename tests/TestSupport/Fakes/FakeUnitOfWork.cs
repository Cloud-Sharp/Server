using CloudSharp.Core.Abstractions.Transactions;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>공유 session의 staged 변경을 flush하고 실패·동시 변경을 주입하는 unit of work.</summary>
public sealed class FakeUnitOfWork(FakePersistenceSession session) : IUnitOfWork
{
    private readonly List<CancellationToken> capturedTokens = new();

    public int SaveChangesCallCount => capturedTokens.Count;

    public IReadOnlyList<CancellationToken> CapturedTokens => capturedTokens;

    public Action<int>? BeforeSaveChanges { get; set; }

    public Func<int, IError?>? SaveChangesFailureFactory { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capturedTokens.Add(cancellationToken);
        BeforeSaveChanges?.Invoke(SaveChangesCallCount);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        var failure = SaveChangesFailureFactory?.Invoke(SaveChangesCallCount);
        return Task.FromResult(failure is null ? session.Flush() : Result.Fail<int>(failure));
    }
}
