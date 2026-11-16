using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceBootstrapWriter : ISpaceBootstrapWriter
{
    private readonly List<FakeSpaceBootstrap> pending = new();
    private readonly List<FakeSpaceBootstrap> added = new();
    private readonly List<FakeSpaceBootstrap> saved = new();

    public Result AddResult { get; set; } = Result.Ok();

    public IReadOnlyList<FakeSpaceBootstrap> Added => added;

    public IReadOnlyList<FakeSpaceBootstrap> Saved => saved;

    public Task<Result> AddAsync(
        long spaceId,
        Guid rootFolderPublicId,
        IReadOnlyList<SpaceRole> roles,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        cancellationToken.ThrowIfCancellationRequested();
        var bootstrap = new FakeSpaceBootstrap(spaceId, rootFolderPublicId, roles.ToArray(), createdAt);
        added.Add(bootstrap);
        if (AddResult.IsSuccess)
        {
            pending.Add(bootstrap);
        }

        return Task.FromResult(AddResult);
    }

    internal int Flush()
    {
        var count = pending.Count;
        saved.AddRange(pending);
        pending.Clear();
        return count;
    }
}

public sealed record FakeSpaceBootstrap(
    long SpaceId,
    Guid RootFolderPublicId,
    IReadOnlyList<SpaceRole> Roles,
    DateTimeOffset CreatedAt);
