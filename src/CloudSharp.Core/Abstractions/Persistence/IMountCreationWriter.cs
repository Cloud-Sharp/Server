using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>새 Mount와 초기 부여를 같은 UnitOfWork에 등록한다.</summary>
public interface IMountCreationWriter
{
    /// <summary>
    /// Mount와 초기 부여를 함께 등록한다.
    /// 저장과 트랜잭션 커밋은 호출한 UseCase가 담당한다.
    /// </summary>
    Task<Result> AddAsync(
        Mount mount,
        CancellationToken cancellationToken = default);
}
