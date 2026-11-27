using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>
/// 정렬된 raw 후보를 반환한다. 접근 가능성 필터링과 응답 페이지 제한은 UseCase가 적용한다.
/// cursor는 저장 계층의 위치 토큰이며, 요청자·조회 조건 묶음과 서명은 API가 맡는다.
/// </summary>
public interface IMountListCandidateReader
{
    Task<Result<MountListCandidatePage>> ReadAsync(
        long spaceId,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default);
}
