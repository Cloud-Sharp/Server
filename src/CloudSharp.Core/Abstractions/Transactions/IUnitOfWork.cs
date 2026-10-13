using FluentResults;

namespace CloudSharp.Core.Abstractions.Transactions;

/// <summary>
/// 현재 유즈케이스에서 등록한 영속성 변경을 저장한다. 저장 시점은 UseCase가 결정하며
/// 트랜잭션 커밋은 ITransactionExecutor가 담당한다.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// 등록된 모든 변경을 저장하고 반영된 행 수를 반환하며 트랜잭션을 커밋하지 않는다.
    /// 알려진 사용자 이메일·사용자명 unique 위반은 <c>USER_EMAIL_CONFLICT</c>·<c>USER_NAME_CONFLICT</c>,
    /// optimistic concurrency 충돌은 <c>PRECONDITION_FAILED</c>, 알려진 저장 의존성 가용성 장애는
    /// <c>DEPENDENCY_UNAVAILABLE</c> 실패로 반환한다. 취소와 예상 밖 예외는 그대로 전파한다.
    /// </summary>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default);
}
