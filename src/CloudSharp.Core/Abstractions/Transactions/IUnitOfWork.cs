using FluentResults;

namespace CloudSharp.Core.Abstractions.Transactions;

/// <summary>
/// 현재 유즈케이스에서 추적한 영속성 변경을 저장한다.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// 추적된 변경을 저장하고 반영된 행 수를 반환한다.
    /// </summary>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default);
}
