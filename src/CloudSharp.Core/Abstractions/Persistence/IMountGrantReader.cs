using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface IMountGrantReader
{
    Task<Result<MountGrantPage>> ReadAsync(
        long spaceId,
        long mountId,
        MountGrantTargetType targetType,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default);
}
