using CloudSharp.Core.Abstractions.Persistence;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountDetailsReader : IMountDetailsReader
{
    public Task<Result<MountDetailsSnapshot?>> ReadAsync(
        long spaceId,
        Guid mountPublicId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
