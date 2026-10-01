using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="IUserRepository"/>. uniqueness 사전 확인 결과, 저장 실패, 조회 결과
/// override를 구성할 수 있고 확인·추가·조회·저장 호출을 기록해 use case와의 계약을 검증한다.
/// Id 조회는 저장 상태(<see cref="PersistedUsersById"/>)와 분리된 복사본을 반환하고,
/// by-id 저장은 조회 시점에 캡처한 버전과 저장 상태의 버전이 다르면 저장소를 바꾸지 않고
/// <c>PRECONDITION_FAILED</c>를 반환한다. 등록 flow는 AddAsync 후 SaveAsync가 DB가 부여한
/// <see cref="AssignedId"/>로 복원해 반환하는 기존 동작을 유지한다.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<string> _checkedNormalizedEmails = new();
    private readonly List<string> _checkedNormalizedUserNames = new();
    private readonly List<string> _searchedNormalizedEmails = new();
    private readonly List<long> _searchedIds = new();
    private readonly List<User> _addedUsers = new();
    private readonly List<User> _savedByIdUsers = new();
    private readonly Dictionary<long, User> _storedUsersById = new();
    private readonly Dictionary<long, long> _versionAtLookupById = new();

    private User? _trackedByIdUser;

    private User? _foundByIdUser;

    public bool EmailExists { get; set; }

    public bool UserNameExists { get; set; }

    /// <summary>사전 uniqueness 확인 단계가 반환할 오류. null이면 정상 동작한다.</summary>
    public Error? ExistenceCheckFailure { get; set; }

    /// <summary>이메일 조회가 반환할 오류. null이면 정상 동작한다.</summary>
    public Error? FindByEmailFailure { get; set; }

    /// <summary>이메일 조회가 던질 예외. null이면 정상 동작한다.</summary>
    public Exception? FindByEmailException { get; set; }

    /// <summary>이메일 조회가 반환할 사용자. null이면 계정이 없는 것으로 처리한다.</summary>
    public User? FoundUser { get; set; }

    /// <summary>Id 조회가 반환할 오류. null이면 정상 동작한다.</summary>
    public Error? FindByIdFailure { get; set; }

    /// <summary>
    /// Id 조회로 반환할 사용자. 설정한 사용자는 저장 상태에 seed되고, null을 설정하면 저장 상태를 비운다.
    /// </summary>
    public User? FoundByIdUser
    {
        get => _foundByIdUser;
        set
        {
            _foundByIdUser = value;
            _storedUsersById.Clear();
            _versionAtLookupById.Clear();
            _trackedByIdUser = null;
            if (value is not null)
            {
                _storedUsersById[value.Id] = Copy(value);
            }
        }
    }

    /// <summary>AddAsync가 반환할 결과. 기본값은 성공.</summary>
    public Result AddResult { get; set; } = Result.Ok();

    /// <summary>
    /// SaveAsync가 반환할 결과를 추적된 사용자로부터 계산하는 factory.
    /// null이면 <see cref="AssignedId"/>를 부여한 사용자로 정상 저장한다.
    /// </summary>
    public Func<User, Result<User>>? SaveResultFactory { get; set; }

    /// <summary>정상 저장 시 DB가 부여한 내부 Id로 사용할 값.</summary>
    public long AssignedId { get; set; } = 42L;

    /// <summary>
    /// by-id 저장 진입 시 concurrency 검사 직전에 호출되는 hook.
    /// 조회 이후·저장 이전의 동시 commit을 시뮬레이션한다.
    /// </summary>
    public Action<User>? BeforeSaveById { get; set; }

    /// <summary>
    /// by-id 저장이 실패할지 결정하는 factory. (저장 대상, 1부터 시작하는 by-id 저장 호출 순번)을 받아
    /// 오류를 반환하면 저장 상태를 바꾸지 않고 실패하고, null이면 정상 저장 경로를 따른다.
    /// </summary>
    public Func<User, int, Error?>? SaveByIdErrorFactory { get; set; }

    public IReadOnlyList<string> CheckedNormalizedEmails => _checkedNormalizedEmails;

    public IReadOnlyList<string> CheckedNormalizedUserNames => _checkedNormalizedUserNames;

    public IReadOnlyList<string> SearchedNormalizedEmails => _searchedNormalizedEmails;

    public IReadOnlyList<long> SearchedIds => _searchedIds;

    public IReadOnlyList<User> AddedUsers => _addedUsers;

    /// <summary>by-id 저장 시도 순서 목록. 성공과 실패 구분 없이 기록한다.</summary>
    public IReadOnlyList<User> SavedByIdUsers => _savedByIdUsers;

    /// <summary>저장 상태 뷰. Id 조회가 반환하는 복사본과는 분리되어 있다.</summary>
    public IReadOnlyDictionary<long, User> PersistedUsersById => _storedUsersById;

    public Task<Result<bool>> ExistsByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        _checkedNormalizedEmails.Add(normalizedEmail);
        return Task.FromResult(Check(EmailExists));
    }

    public Task<Result<bool>> ExistsByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        _checkedNormalizedUserNames.Add(normalizedUserName);
        return Task.FromResult(Check(UserNameExists));
    }

    public Task<Result<User?>> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        _searchedNormalizedEmails.Add(normalizedEmail);
        if (FindByEmailException is not null)
        {
            throw FindByEmailException;
        }

        return Task.FromResult(Find());
    }

    public Task<Result<User?>> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _searchedIds.Add(id);

        if (FindByIdFailure is not null)
        {
            return Task.FromResult(Result.Fail<User?>(FindByIdFailure));
        }

        if (!_storedUsersById.TryGetValue(id, out var stored))
        {
            _trackedByIdUser = null;
            return Task.FromResult(Result.Ok<User?>(null));
        }

        var queried = Copy(stored);
        _versionAtLookupById[id] = stored.Version;
        _trackedByIdUser = queried;
        return Task.FromResult(Result.Ok<User?>(queried));
    }

    public Task<Result> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _addedUsers.Add(user);
        return Task.FromResult(AddResult);
    }

    public Task<Result<User>> SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_trackedByIdUser is not null)
        {
            return Task.FromResult(SaveById(_trackedByIdUser));
        }

        var user = _addedUsers.Single();
        if (SaveResultFactory is not null)
        {
            return Task.FromResult(SaveResultFactory(user));
        }

        var persisted = User.Reconstitute(
            AssignedId,
            user.PublicId,
            user.Email,
            user.UserName,
            user.DisplayName,
            user.PasswordHash,
            user.SystemRole,
            user.Status,
            user.SecurityVersion,
            user.Version,
            user.CreatedAt,
            user.UpdatedAt,
            user.SuspendedAt,
            user.DeletedAt);
        return Task.FromResult(Result.Ok(persisted));
    }

    /// <summary>
    /// 저장 상태의 <see cref="User.Version"/>만 1 증가시켜 저장 이전의 동시 변경을 시뮬레이션한다.
    /// </summary>
    public void BumpStoredVersion(long id)
    {
        var stored = GetStored(id);
        _storedUsersById[id] = Copy(stored, version: stored.Version + 1);
    }

    /// <summary>
    /// 저장 상태의 <see cref="User.SecurityVersion"/>과 <see cref="User.Version"/>을 1 증가시켜
    /// 저장 이전의 동시 전체 세션 폐기를 시뮬레이션한다.
    /// </summary>
    public void BumpStoredSecurityVersion(long id)
    {
        var stored = GetStored(id);
        _storedUsersById[id] = Copy(
            stored,
            securityVersion: stored.SecurityVersion + 1,
            version: stored.Version + 1);
    }

    private User GetStored(long id) =>
        _storedUsersById.TryGetValue(id, out var stored)
            ? stored
            : throw new InvalidOperationException($"No stored user with id {id}.");

    private Result<User> SaveById(User user)
    {
        _savedByIdUsers.Add(user);
        BeforeSaveById?.Invoke(user);

        if (SaveByIdErrorFactory is not null)
        {
            var error = SaveByIdErrorFactory(user, _savedByIdUsers.Count);
            if (error is not null)
            {
                return Result.Fail<User>(error);
            }
        }

        if (!_versionAtLookupById.TryGetValue(user.Id, out var versionAtLookup)
            || !_storedUsersById.TryGetValue(user.Id, out var stored)
            || stored.Version != versionAtLookup)
        {
            return Result.Fail<User>(CommonError.PreconditionFailed());
        }

        var persisted = Copy(user);
        _storedUsersById[user.Id] = persisted;
        return Result.Ok(Copy(persisted));
    }

    private static User Copy(User user, long? securityVersion = null, long? version = null) =>
        User.Reconstitute(
            user.Id,
            user.PublicId,
            user.Email,
            user.UserName,
            user.DisplayName,
            user.PasswordHash,
            user.SystemRole,
            user.Status,
            securityVersion ?? user.SecurityVersion,
            version ?? user.Version,
            user.CreatedAt,
            user.UpdatedAt,
            user.SuspendedAt,
            user.DeletedAt);

    private Result<bool> Check(bool exists) =>
        ExistenceCheckFailure is null
            ? Result.Ok(exists)
            : Result.Fail<bool>(ExistenceCheckFailure);

    private Result<User?> Find() =>
        FindByEmailFailure is null
            ? Result.Ok<User?>(FoundUser)
            : Result.Fail<User?>(FindByEmailFailure);
}