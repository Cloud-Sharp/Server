using System.Data;
using CloudSharp.Core.Abstractions.Transactions;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="ITransactionExecutor"/>. 실제 DB 없이 operation을 즉시 실행하고
/// 성공 결과는 커밋, 실패 결과와 예외는 롤백으로 간주해 횟수를 기록한다.
/// 진입 시 토큰이 이미 취소되었으면 실제 adapter처럼 <see cref="OperationCanceledException"/>을 던진다.
/// </summary>
public sealed class FakeTransactionExecutor : ITransactionExecutor
{
    private int _commitCount;
    private int _rollbackCount;

    /// <summary>커밋 직후 호출되는 hook. 커밋과 후속 작업 사이의 시각 흐름을 시뮬레이션할 때 사용한다.</summary>
    public Action? OnCommitted { get; set; }

    public int CommitCount => _commitCount;

    public int RollbackCount => _rollbackCount;

    public async Task<Result> ExecuteAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await operation(cancellationToken);
            Record(result.IsFailed);
            return result;
        }
        catch
        {
            _rollbackCount++;
            throw;
        }
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await operation(cancellationToken);
            Record(result.IsFailed);
            return result;
        }
        catch
        {
            _rollbackCount++;
            throw;
        }
    }

    private void Record(bool failed)
    {
        if (failed)
        {
            _rollbackCount++;
            return;
        }

        _commitCount++;
        OnCommitted?.Invoke();
    }
}