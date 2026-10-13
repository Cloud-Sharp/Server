using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>
/// User aggregate 영속성 port. 구현체는 Infrastructure의 EF Core adapter이며
/// 모든 메서드는 <see cref="ITransactionExecutor"/>가 연 트랜잭션 안에서만 호출된다.
/// 조회는 독립적인 도메인 snapshot을 반환하고, Add/Update는 변경만 등록한다.
/// 구현체는 호출 사이에 저장 대상을 기억하지 않으며 저장·커밋하지 않는다.
/// 알려진 조회 의존성 가용성 장애는 <c>DEPENDENCY_UNAVAILABLE</c> 오류로 반환한다.
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
    /// 내부 Id로 계정의 독립적인 도메인 snapshot을 조회한다.
    /// 계정이 없으면 실패 없이 <c>null</c>을 값으로 반환하며,
    /// 알려진 의존성 가용성 장애는 실패 결과로 반환한다.
    /// 조회만 수행하고 트랜잭션을 커밋하지 않는다.
    /// </summary>
    Task<Result<User?>> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PublicId로 계정의 독립적인 도메인 snapshot을 조회한다.
    /// 계정이 없으면 실패 없이 <c>null</c>을 반환한다. 같은 트랜잭션에서 UnitOfWork로 저장한
    /// 신규 사용자를 다시 조회하면 DB가 생성한 내부 Id를 포함한 상태를 반환한다.
    /// 알려진 의존성 가용성 장애는 실패 결과로 반환한다.
    /// </summary>
    Task<Result<User?>> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 새 사용자의 추가를 등록한다. 아직 저장소에는 반영하지 않고 입력 객체의 Id도 변경하지 않는다.
    /// UseCase가 IUnitOfWork로 저장한 뒤 PublicId로 재조회해 DB가 생성한 내부 Id를 얻는다.
    /// </summary>
    Task<Result> AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 지정한 기존 사용자의 변경을 등록한다. 도메인 변경 전 조회한 버전을 expectedVersion으로 전달하며,
    /// 변경된 User.Version에서 원래 버전을 역산하지 않는다. 조회 순서나 이전 저장 대상에 의존하지 않는다.
    /// 실제 버전 충돌 검사는 UseCase가 IUnitOfWork로 저장할 때 수행하고,
    /// 변경이 없는 경우에도 버전 조건을 검사한다. 충돌은 <c>PRECONDITION_FAILED</c>로 반환한다.
    /// </summary>
    Task<Result> UpdateAsync(
        User user,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
