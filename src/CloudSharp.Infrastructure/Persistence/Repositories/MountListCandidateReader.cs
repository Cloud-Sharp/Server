using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountListCandidateReader : IMountListCandidateReader
{
    public Task<Result<MountListCandidatePage>> ReadAsync(
        long spaceId,
        CurrentActor actor,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
