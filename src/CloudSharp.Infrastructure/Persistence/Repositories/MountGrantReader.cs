using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountGrantReader : IMountGrantReader
{
    public Task<Result<MountGrantPage>> ReadAsync(
        long spaceId,
        long mountId,
        MountGrantTargetType targetType,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
