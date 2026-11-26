using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface IMountReader
{
    Task<Result<Mount?>> ReadAsync(
        long spaceId,
        Guid mountPublicId,
        CancellationToken cancellationToken = default);
}
