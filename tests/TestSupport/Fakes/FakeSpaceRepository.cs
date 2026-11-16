using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceRepository : ISpaceRepository
{
    private readonly Dictionary<long, Space> persisted = new();
    private readonly List<Space> pendingAdds = new();
    private readonly List<FakeSpaceUpdate> pendingUpdates = new();
    private readonly List<Space> addedSpaces = new();
    private readonly List<FakeSpaceUpdate> updateCalls = new();
    private readonly List<long> searchedIds = new();
    private readonly List<Guid> searchedPublicIds = new();
    private long generatedIdCount;

    public long AssignedId { get; set; } = 1001L;

    public Result AddResult { get; set; } = Result.Ok();

    public Result UpdateResult { get; set; } = Result.Ok();

    public Error? FindByIdFailure { get; set; }

    public IReadOnlyDictionary<long, Space> PersistedSpacesById => persisted;

    public IReadOnlyList<Space> AddedSpaces => addedSpaces;

    public IReadOnlyList<FakeSpaceUpdate> UpdateCalls => updateCalls;

    public IReadOnlyList<long> SearchedIds => searchedIds;

    public IReadOnlyList<Guid> SearchedPublicIds => searchedPublicIds;

    public void Seed(Space space)
    {
        ArgumentNullException.ThrowIfNull(space);
        persisted[space.Id] = Copy(space);
    }

    public Task<Result<Space?>> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchedIds.Add(id);
        return Task.FromResult(FindByIdFailure is not null
            ? Result.Fail<Space?>(FindByIdFailure)
            : Result.Ok<Space?>(
                persisted.TryGetValue(id, out var space) ? Copy(space) : null));
    }

    public Task<Result<Space?>> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchedPublicIds.Add(publicId);
        var space = persisted.Values.SingleOrDefault(candidate => candidate.PublicId == publicId);
        return Task.FromResult(Result.Ok<Space?>(space is null ? null : Copy(space)));
    }

    public Task<Result> AddAsync(Space space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        cancellationToken.ThrowIfCancellationRequested();
        addedSpaces.Add(Copy(space));
        if (AddResult.IsSuccess)
        {
            pendingAdds.Add(Copy(space));
        }

        return Task.FromResult(AddResult);
    }

    public Task<Result> UpdateAsync(
        Space space,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        cancellationToken.ThrowIfCancellationRequested();
        var update = new FakeSpaceUpdate(Copy(space), expectedVersion);
        updateCalls.Add(update);
        if (UpdateResult.IsSuccess)
        {
            pendingUpdates.Add(update);
        }

        return Task.FromResult(UpdateResult);
    }

    internal Result<int> Flush()
    {
        var writes = new Dictionary<long, Space>();
        foreach (var update in pendingUpdates)
        {
            if (!persisted.TryGetValue(update.Space.Id, out var current)
                || current.Version != update.ExpectedVersion)
            {
                return Result.Fail<int>(CommonError.PreconditionFailed());
            }

            writes[update.Space.Id] = Copy(update.Space);
        }

        foreach (var added in pendingAdds)
        {
            var id = AssignedId + generatedIdCount++;
            while (persisted.ContainsKey(id) || writes.ContainsKey(id))
            {
                id = AssignedId + generatedIdCount++;
            }

            writes[id] = Copy(added, id);
        }

        foreach (var write in writes)
        {
            persisted[write.Key] = write.Value;
        }

        pendingAdds.Clear();
        pendingUpdates.Clear();
        return Result.Ok(writes.Count);
    }

    internal static Space Copy(Space space, long? id = null) =>
        Space.Reconstitute(
            id ?? space.Id,
            space.PublicId,
            space.OwnerUserId,
            space.Name,
            space.Description,
            space.Status,
            space.AllowedBytes,
            space.UsedBytes,
            space.ReservedBytes,
            space.Version,
            space.CreatedAt,
            space.UpdatedAt,
            space.ArchivedAt,
            space.DeletingAt,
            space.DeletedAt);
}

public sealed record FakeSpaceUpdate(Space Space, long ExpectedVersion);
