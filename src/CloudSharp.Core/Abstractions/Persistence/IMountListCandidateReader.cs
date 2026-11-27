using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>
/// 정렬된 raw 후보를 반환한다. 접근 가능성 필터링과 응답 페이지 제한은 UseCase가 적용한다.
/// cursor는 Space와 요청 사용자 내부 ID에 묶는다.
/// </summary>
public interface IMountListCandidateReader
{
    Task<Result<MountListCandidatePage>> ReadAsync(
        long spaceId,
        long userId,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default);
}
