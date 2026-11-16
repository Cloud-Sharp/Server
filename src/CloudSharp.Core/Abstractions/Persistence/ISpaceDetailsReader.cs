using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>인가 이후 Space의 최신 상태·버전과 외부 소유자·루트 폴더 식별자를 조회한다.</summary>
public interface ISpaceDetailsReader
{
    Task<Result<SpaceDetailsSnapshot?>> ReadAsync(
        long spaceId,
        CancellationToken cancellationToken = default);
}
