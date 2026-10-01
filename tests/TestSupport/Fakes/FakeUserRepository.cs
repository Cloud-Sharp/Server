using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="IUserRepository"/>. uniqueness 사전 확인 결과, 저장 실패, 저장 결과
/// override를 구성할 수 있고 확인·추가 호출을 기록해 use case와의 계약을 검증한다.
/// 기본 동작은 AddAsync로 추적된 사용자를 <see cref="User.Reconstitute"/>로
/// <see cref="AssignedId"/>와 함께 복원해 반환한다.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<string> _checkedNormalizedEmails = new();
    private readonly List<string> _checkedNormalizedUserNames = new();
    private readonly List<User> _addedUsers = new();

    public bool EmailExists { get; set; }

    public bool UserNameExists { get; set; }

    /// <summary>사전 uniqueness 확인 단계가 반환할 오류. null이면 정상 동작한다.</summary>
    public Error? ExistenceCheckFailure { get; set; }

    /// <summary>AddAsync가 반환할 결과. 기본값은 성공.</summary>
    public Result AddResult { get; set; } = Result.Ok();

    /// <summary>
    /// SaveAsync가 반환할 결과를 추적된 사용자로부터 계산하는 factory.
    /// null이면 <see cref="AssignedId"/>를 부여한 사용자로 정상 저장한다.
    /// </summary>
    public Func<User, Result<User>>? SaveResultFactory { get; set; }

    /// <summary>정상 저장 시 DB가 부여한 내부 Id로 사용할 값.</summary>
    public long AssignedId { get; set; } = 42L;

    public IReadOnlyList<string> CheckedNormalizedEmails => _checkedNormalizedEmails;

    public IReadOnlyList<string> CheckedNormalizedUserNames => _checkedNormalizedUserNames;

    public IReadOnlyList<User> AddedUsers => _addedUsers;

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

    public Task<Result> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _addedUsers.Add(user);
        return Task.FromResult(AddResult);
    }

    public Task<Result<User>> SaveAsync(CancellationToken cancellationToken = default)
    {
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

    private Result<bool> Check(bool exists) =>
        ExistenceCheckFailure is null
            ? Result.Ok(exists)
            : Result.Fail<bool>(ExistenceCheckFailure);
}