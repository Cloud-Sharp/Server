using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>
/// User aggregate 영속성 port. 구현체는 Infrastructure의 EF Core adapter이며
/// 모든 메서드는 <see cref="ITransactionExecutor"/>가 연 트랜잭션 안에서만 호출된다.
/// 트랜잭션은 커밋하지 않고, 알려진 의존성 가용성 장애는 <c>DEPENDENCY_UNAVAILABLE</c> 오류로 반환한다.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// 정규화된 이메일(trim·uppercased 비교값)로 계정 존재 여부를 확인한다.
    /// </summary>
    Task<Result<bool>> ExistsByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 정규화된 사용자명(trim·uppercased 비교값)으로 계정 존재 여부를 확인한다.
    /// </summary>
    Task<Result<bool>> ExistsByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 정규화된 이메일(trim·uppercased 비교값)로 계정을 조회한다.
    /// 계정이 없으면 실패 없이 <c>null</c>을 값으로 반환하며,
    /// 알려진 의존성 가용성 장애는 실패 결과로 반환한다.
    /// 조회만 수행하고 트랜잭션을 커밋하지 않는다.
    /// </summary>
    Task<Result<User?>> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 새 사용자를 변경 추적 대상에 추가한다. 아직 저장소에는 반영하지 않는다.
    /// </summary>
    Task<Result> AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 추적된 변경을 저장소에 반영하고 DB가 생성한 양수 내부 Id가 부여된 <see cref="User"/>를 반환한다.
    /// 호출부의 트랜잭션은 커밋하지 않는다. 구현체는 <see cref="IUnitOfWork"/>로 저장하고
    /// <see cref="User.Reconstitute"/>로 저장된 상태를 복원하며, 이메일·사용자명 unique constraint
    /// 위반은 각각 <c>USER_EMAIL_CONFLICT</c>, <c>USER_NAME_CONFLICT</c> 오류로 변환한다.
    /// </summary>
    Task<Result<User>> SaveAsync(CancellationToken cancellationToken = default);
}