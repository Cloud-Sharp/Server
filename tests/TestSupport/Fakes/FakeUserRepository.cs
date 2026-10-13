using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>조회 snapshot과 staged Add/Update를 제공하는 테스트용 user repository.</summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly FakePersistenceSession session;
    private readonly List<string> checkedNormalizedEmails = new();
    private readonly List<string> checkedNormalizedUserNames = new();
    private readonly List<string> searchedNormalizedEmails = new();
    private readonly List<long> searchedIds = new();
    private readonly List<Guid> searchedPublicIds = new();
    private readonly List<User> addedUsers = new();
    private readonly List<FakeUserUpdate> updateCalls = new();
    private User? foundByIdUser;

    public FakeUserRepository() : this(new FakePersistenceSession()) { }

    public FakeUserRepository(FakePersistenceSession session) => this.session = session;

    public bool EmailExists { get; set; }

    public bool UserNameExists { get; set; }

    public Error? ExistenceCheckFailure { get; set; }

    public Error? FindByEmailFailure { get; set; }

    public Exception? FindByEmailException { get; set; }

    public User? FoundUser { get; set; }

    public Error? FindByIdFailure { get; set; }

    public Exception? FindByIdException { get; set; }

    public Error? FindByPublicIdFailure { get; set; }

    public Func<Guid, Result<User?>>? FindByPublicIdResultFactory { get; set; }

    public User? FoundByIdUser
    {
        get => foundByIdUser;
        set
        {
            foundByIdUser = value;
            session.ReplaceSeed(value);
        }
    }

    public Result AddResult { get; set; } = Result.Ok();

    public Result UpdateResult { get; set; } = Result.Ok();

    public long AssignedId
    {
        get => session.AssignedId;
        set => session.AssignedId = value;
    }

    public IReadOnlyList<string> CheckedNormalizedEmails => checkedNormalizedEmails;

    public IReadOnlyList<string> CheckedNormalizedUserNames => checkedNormalizedUserNames;

    public IReadOnlyList<string> SearchedNormalizedEmails => searchedNormalizedEmails;

    public IReadOnlyList<long> SearchedIds => searchedIds;

    public IReadOnlyList<Guid> SearchedPublicIds => searchedPublicIds;

    public IReadOnlyList<User> AddedUsers => addedUsers;

    public IReadOnlyList<FakeUserUpdate> UpdateCalls => updateCalls;

    public IReadOnlyDictionary<long, User> PersistedUsersById => session.PersistedUsersById;

    public Task<Result<bool>> ExistsByNormalizedEmailAsync(string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkedNormalizedEmails.Add(normalizedEmail);
        return Task.FromResult(Check(EmailExists || session.FindByNormalizedEmail(normalizedEmail) is not null));
    }

    public Task<Result<bool>> ExistsByNormalizedUserNameAsync(string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkedNormalizedUserNames.Add(normalizedUserName);
        return Task.FromResult(Check(UserNameExists || session.ExistsByNormalizedUserName(normalizedUserName)));
    }

    public Task<Result<User?>> FindByNormalizedEmailAsync(string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchedNormalizedEmails.Add(normalizedEmail);
        if (FindByEmailException is not null)
        {
            throw FindByEmailException;
        }

        return Task.FromResult(FindByEmailFailure is not null
            ? Result.Fail<User?>(FindByEmailFailure)
            : Result.Ok(FoundUser is null
                ? session.FindByNormalizedEmail(normalizedEmail)
                : FakePersistenceSession.Copy(FoundUser)));
    }

    public Task<Result<User?>> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchedIds.Add(id);
        if (FindByIdException is not null)
        {
            throw FindByIdException;
        }

        return Task.FromResult(FindByIdFailure is not null
            ? Result.Fail<User?>(FindByIdFailure)
            : Result.Ok(session.FindById(id)));
    }

    public Task<Result<User?>> FindByPublicIdAsync(Guid publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        searchedPublicIds.Add(publicId);
        if (FindByPublicIdResultFactory is not null)
        {
            return Task.FromResult(FindByPublicIdResultFactory(publicId));
        }

        return Task.FromResult(FindByPublicIdFailure is not null
            ? Result.Fail<User?>(FindByPublicIdFailure)
            : Result.Ok(session.FindByPublicId(publicId)));
    }

    public Task<Result> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        addedUsers.Add(FakePersistenceSession.Copy(user));
        if (AddResult.IsSuccess)
        {
            session.StageAdd(user);
        }

        return Task.FromResult(AddResult);
    }

    public Task<Result> UpdateAsync(User user, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        updateCalls.Add(new FakeUserUpdate(FakePersistenceSession.Copy(user), expectedVersion));
        if (UpdateResult.IsSuccess)
        {
            session.StageUpdate(user, expectedVersion);
        }

        return Task.FromResult(UpdateResult);
    }

    public void BumpStoredVersion(long id) => session.BumpStoredVersion(id);

    public void BumpStoredSecurityVersion(long id) => session.BumpStoredSecurityVersion(id);

    private Result<bool> Check(bool exists) => ExistenceCheckFailure is null
        ? Result.Ok(exists)
        : Result.Fail<bool>(ExistenceCheckFailure);
}
