using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>기존 Mount와 명시적 부여 변경을 같은 UnitOfWork에 등록한다.</summary>
public interface IMountMutationWriter
{
    /// <summary>
    /// 조회 시 관찰한 버전을 낙관적 동시성 원본 값으로 사용해 Mount와 부여 변경을 등록한다.
    /// 저장과 트랜잭션 커밋은 호출한 UseCase가 담당한다.
    /// </summary>
    Task<Result> UpdateAsync(
        Mount mount,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
