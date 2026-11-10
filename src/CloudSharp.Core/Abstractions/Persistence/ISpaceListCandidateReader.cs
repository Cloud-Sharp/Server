using CloudSharp.Core.Common.Actors;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>정렬된 raw 후보를 반환한다. 접근 가능성 필터링과 응답 페이지 제한은 UseCase가 적용한다.</summary>
public interface ISpaceListCandidateReader
{
    Task<Result<SpaceListCandidatePage>> ReadAsync(
        CurrentActor actor,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default);
}
