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
}