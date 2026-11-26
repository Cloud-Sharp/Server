using CloudSharp.Core.Abstractions.Persistence;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountCreationGrantTargetReader : IMountCreationGrantTargetReader
{
    public Task<Result<MountCreationGrantTargets>> ReadAsync(
        long spaceId,
        IReadOnlyCollection<Guid> rolePublicIds,
        IReadOnlyCollection<Guid> userPublicIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
