using CloudSharp.Core.Domain.Spaces;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>조회는 독립 snapshot을 반환하고 Add/Update는 IUnitOfWork가 저장할 변경만 등록한다.</summary>
public interface ISpaceRepository
{
    Task<Result<Space?>> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Result<Space?>> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(Space space, CancellationToken cancellationToken = default);

    /// <summary>조회 시 관찰한 버전을 expectedVersion으로 전달하고 충돌은 저장 시 PRECONDITION_FAILED로 반환한다.</summary>
    Task<Result> UpdateAsync(
        Space space,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
