using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceAuthorizationReader : ISpaceAuthorizationReader
{
    private readonly Queue<SpaceAuthorizationSnapshot?> snapshots;
    private readonly List<(Guid SpacePublicId, long UserId)> reads = [];
    private SpaceAuthorizationSnapshot? current;

    public FakeSpaceAuthorizationReader(params SpaceAuthorizationSnapshot?[] snapshots)
    {
        this.snapshots = new Queue<SpaceAuthorizationSnapshot?>(snapshots);
    }

    public IReadOnlyList<(Guid SpacePublicId, long UserId)> Reads => reads;

    public Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
        Guid spacePublicId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reads.Add((spacePublicId, userId));
        if (snapshots.Count > 0)
        {
            current = snapshots.Dequeue();
        }

        return Task.FromResult(Result.Ok(current));
    }
}
