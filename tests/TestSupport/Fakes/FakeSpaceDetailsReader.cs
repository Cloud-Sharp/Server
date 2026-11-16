using CloudSharp.Core.Abstractions.Persistence;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceDetailsReader : ISpaceDetailsReader
{
    private readonly List<long> readSpaceIds = new();

    public SpaceDetailsSnapshot? Snapshot { get; set; }

    public IReadOnlyList<long> ReadSpaceIds => readSpaceIds;

    public Task<Result<SpaceDetailsSnapshot?>> ReadAsync(
        long spaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        readSpaceIds.Add(spaceId);
        var snapshot = Snapshot is null
            ? null
            : new SpaceDetailsSnapshot(
                FakeSpaceRepository.Copy(Snapshot.Space),
                Snapshot.OwnerUserPublicId,
                Snapshot.RootFolderPublicId);
        return Task.FromResult(Result.Ok<SpaceDetailsSnapshot?>(snapshot));
    }
}
