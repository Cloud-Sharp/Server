using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Auth;

/// <summary>
/// UserSession 저장 port. 구현체는 Infrastructure의 Redis adapter이며
/// 세션 본문 저장, 사용자별 session index 갱신, 최대 활성 세션 수(10개) 초과분의 오래된 순 제거를
/// 하나의 원자적 연산(Lua script 등)으로 수행해야 한다.
/// 평문 token은 절대 다루지 않고 <see cref="UserSession"/>의 token hash만 저장하며,
/// 알려진 의존성 가용성 장애는 <c>DEPENDENCY_UNAVAILABLE</c> 오류로 반환한다.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// 세션을 저장한다. 저장에 실패하면 등록·로그인 flow는 성공으로 응답할 수 없다.
    /// </summary>
    Task<Result> StoreAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// token hash로 저장된 세션을 조회한다. 세션이 없으면 실패 없이 <c>null</c>을 값으로 반환하며,
    /// 알려진 의존성 가용성 장애는 실패 결과로 반환한다.
    /// 조회만 수행하며 반환된 <see cref="UserSession"/>은 저장 상태의 복사본을 다룬 것과 동일하게 취급한다.
    /// </summary>
    Task<Result<UserSession?>> FindByTokenHashAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 세션을 삭제한다(본문 key와 사용자별 session index member). 이미 삭제된 세션은
    /// 멱등 성공으로 처리한다. 알려진 의존성 가용성 장애는 실패 결과로 반환하며,
    /// 이 경우 삭제가 이루어졌는지 보장할 수 없다.
    /// </summary>
    Task<Result> RemoveAsync(
        UserSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 비밀번호 변경 commit 후처리를 하나의 원자적 연산으로 수행한다.
    /// 저장된 현재 세션이 <paramref name="currentSession"/>의 token hash·소유자·세션 ID·
    /// <see cref="UserSession.SecurityVersion"/>(변경 전 값)·만료와 일치하면
    /// 본문의 보안 버전만 <paramref name="newSecurityVersion"/>로 교체하고
    /// 같은 사용자의 나머지 세션을 모두 삭제한다. token, 세션 ID, 발급·활동·만료 시각은 그대로 유지한다.
    /// 현재 세션이 이미 삭제·만료되었거나 상태가 달라졌으면 현재 세션을 다시 만들거나 되돌리지 않고
    /// 나머지 세션만 삭제한 뒤 <c>false</c>를 값으로 반환한다. 보안 버전은 절대 뒤로 이동시키지 않는다.
    /// 알려진 의존성 가용성 장애는 실패 결과로 반환하며, 이 경우 후처리 적용 여부를 보장할 수 없다.
    /// </summary>
    Task<Result<bool>> FinalizePasswordChangeAsync(
        UserSession currentSession,
        long newSecurityVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}