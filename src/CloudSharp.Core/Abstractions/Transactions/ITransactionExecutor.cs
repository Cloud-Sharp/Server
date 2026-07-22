using System.Data;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Transactions;

/// <summary>
/// 여러 데이터베이스 작업을 하나의 트랜잭션으로 실행한다.
/// </summary>
public interface ITransactionExecutor
{
    /// <summary>
    /// 작업을 트랜잭션 안에서 실행한다.
    /// 성공 결과는 커밋하고 실패 결과는 롤백한다.
    /// </summary>
    Task<Result> ExecuteAsync(
        Func<CancellationToken, Task<Result>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 값을 반환하는 작업을 트랜잭션 안에서 실행한다.
    /// 성공 결과는 커밋하고 실패 결과는 롤백한다.
    /// </summary>
    Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
