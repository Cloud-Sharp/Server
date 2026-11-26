using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>새 Mount와 초기 부여, 루트 콘텐츠를 같은 UnitOfWork에 등록한다.</summary>
public interface IMountCreationWriter
{
    /// <summary>
    /// Mount·초기 부여·루트를 함께 등록하고 루트 Folder PublicId를 반환한다.
    /// 저장과 트랜잭션 커밋은 호출한 UseCase가 담당한다.
    /// </summary>
    Task<Result<Guid>> AddAsync(
        Mount mount,
        long creatorUserId,
        CancellationToken cancellationToken = default);
}
