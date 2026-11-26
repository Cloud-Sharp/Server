using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountReader : IMountReader
{
    public Task<Result<Mount?>> ReadAsync(
        long spaceId,
        Guid mountPublicId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
