using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface IMountDetailsReader
{
    Task<Result<MountDetailsSnapshot?>> ReadAsync(
        long spaceId,
        Guid mountPublicId,
        CancellationToken cancellationToken = default);
}
