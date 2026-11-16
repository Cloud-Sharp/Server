using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface ISpaceInviteReader
{
    Task<Result<SpaceInviteReadPage>> ReadPageAsync(
        long spaceId,
        string? cursor,
        int limit,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default);
}
