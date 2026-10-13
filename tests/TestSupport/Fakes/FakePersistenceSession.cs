using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// Repository, unit of work, transaction executor가 공유하는 테스트용 영속성 상태.
/// 트랜잭션의 미확정 변경과 다른 writer가 확정한 상태를 분리한다.
/// </summary>
public sealed class FakePersistenceSession
{
    private readonly Dictionary<long, User> committed = new();
    private readonly List<User> pendingAdds = new();
    private readonly Dictionary<long, FakeUserUpdate> pendingUpdates = new();
    private readonly Dictionary<long, User> flushed = new();
    private long generatedIdCount;

    public long AssignedId { get; set; } = 42L;

    public IReadOnlyDictionary<long, User> PersistedUsersById => committed;

    public bool IsTransactionActive { get; private set; }

    public void Seed(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        committed[user.Id] = Copy(user);
    }

    internal void ReplaceSeed(User? user)
    {
        committed.Clear();
        ClearPending();
        if (user is not null)
        {
            Seed(user);
        }
    }

    internal User? FindById(long id) =>
        flushed.TryGetValue(id, out var ownWrite) ? Copy(ownWrite)
        : committed.TryGetValue(id, out var stored) ? Copy(stored)
        : null;

    internal User? FindByPublicId(Guid publicId)
    {
        var found = VisibleUsers().SingleOrDefault(user => user.PublicId == publicId);
        return found is null ? null : Copy(found);
    }

    internal User? FindByNormalizedEmail(string normalizedEmail)
    {
        var found = VisibleUsers().SingleOrDefault(user => user.Email.Normalized == normalizedEmail);
        return found is null ? null : Copy(found);
    }

    internal bool ExistsByNormalizedUserName(string normalizedUserName) =>
        VisibleUsers().Any(user => user.UserName?.Normalized == normalizedUserName);

    internal void StageAdd(User user) => pendingAdds.Add(Copy(user));

    internal void StageUpdate(User user, long originalVersion) =>
        pendingUpdates[user.Id] = new FakeUserUpdate(Copy(user), originalVersion);

    internal Result<int> Flush()
    {
        if (!IsTransactionActive)
        {
            throw new InvalidOperationException("Fake unit of work requires an active transaction.");
        }

        var writes = new Dictionary<long, User>();
        foreach (var update in pendingUpdates.Values)
        {
            var current = FindById(update.User.Id);
            if (current is null || current.Version != update.OriginalVersion)
            {
                return Result.Fail<int>(CommonError.PreconditionFailed());
            }

            writes[update.User.Id] = Copy(update.User);
        }

        foreach (var added in pendingAdds)
        {
            var id = AssignedId + generatedIdCount++;
            while (committed.ContainsKey(id) || flushed.ContainsKey(id) || writes.ContainsKey(id))
            {
                id = AssignedId + generatedIdCount++;
            }

            writes[id] = Copy(added, id: id);
        }

        foreach (var write in writes)
        {
            flushed[write.Key] = write.Value;
        }

        pendingAdds.Clear();
        pendingUpdates.Clear();
        return Result.Ok(writes.Count);
    }

    internal void Begin()
    {
        if (IsTransactionActive)
        {
            throw new InvalidOperationException("Nested database transactions are not supported.");
        }

        ClearPending();
        IsTransactionActive = true;
    }

    internal void Commit()
    {
        foreach (var write in flushed)
        {
            committed[write.Key] = Copy(write.Value);
        }

        ClearPending();
        IsTransactionActive = false;
    }

    internal void Rollback()
    {
        ClearPending();
        IsTransactionActive = false;
    }

    public void BumpStoredVersion(long id)
    {
        var stored = GetStored(id);
        committed[id] = Copy(stored, version: stored.Version + 1);
    }

    public void BumpStoredSecurityVersion(long id)
    {
        var stored = GetStored(id);
        committed[id] = Copy(stored,
            securityVersion: stored.SecurityVersion + 1,
            version: stored.Version + 1);
    }

    private IEnumerable<User> VisibleUsers() =>
        flushed.Values.Concat(committed.Values.Where(user => !flushed.ContainsKey(user.Id)));

    private User GetStored(long id) => committed.TryGetValue(id, out var stored)
        ? stored
        : throw new InvalidOperationException($"No stored user with id {id}.");

    private void ClearPending()
    {
        pendingAdds.Clear();
        pendingUpdates.Clear();
        flushed.Clear();
    }

    internal static User Copy(User user, long? id = null, long? securityVersion = null, long? version = null) =>
        User.Reconstitute(
            id ?? user.Id, user.PublicId, user.Email, user.UserName, user.DisplayName,
            user.PasswordHash, user.SystemRole, user.Status, securityVersion ?? user.SecurityVersion,
            version ?? user.Version, user.CreatedAt, user.UpdatedAt, user.SuspendedAt, user.DeletedAt);
}

public sealed record FakeUserUpdate(User User, long OriginalVersion);
