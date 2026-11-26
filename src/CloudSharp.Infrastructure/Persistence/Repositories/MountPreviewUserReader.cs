using CloudSharp.Core.Abstractions.Persistence;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountPreviewUserReader : IMountPreviewUserReader
{
    public Task<Result<MountPreviewUserSnapshot?>> ReadUserAsync(
        long spaceId,
        Guid userPublicId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<MountPreviewCandidatePage>> ReadCandidatesAsync(
        long spaceId,
        string? cursor,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
