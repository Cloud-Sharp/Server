using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface IMountPreviewUserReader
{
    Task<Result<MountPreviewUserSnapshot?>> ReadUserAsync(
        long spaceId,
        Guid userPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>User PublicId 순 raw 후보를 반환한다. 권한 변화 필터링과 응답 페이지 제한은 UseCase가 적용한다.</summary>
    Task<Result<MountPreviewCandidatePage>> ReadCandidatesAsync(
        long spaceId,
        string? cursor,
        int batchSize,
        CancellationToken cancellationToken = default);
}
