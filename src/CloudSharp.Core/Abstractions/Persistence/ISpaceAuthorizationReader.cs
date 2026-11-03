using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>
/// 인가에 필요한 Space·사용자 Membership·연결 Role을 일관된 조회 범위에서 읽는 port.
/// 구현체는 저장 상태 및 다른 호출과 독립된 도메인 snapshot을 반환하며 조회 결과를 캐시하지 않는다.
/// </summary>
public interface ISpaceAuthorizationReader
{
    /// <summary>
    /// 지정한 Space와 사용자의 현재 snapshot을 조회한다. Space 미존재는 성공한 null 결과다.
    /// Membership이 없거나 비활성이어도 Space는 반환하며 자격 판단은 Core 정책이 수행한다.
    /// 알려진 가용성 장애는 DEPENDENCY_UNAVAILABLE로 반환하고 취소·예상하지 못한 오류는 전파한다.
    /// 저장·커밋하지 않으며 조회에 필요한 연결과 일관성 범위는 구현체가 관리한다.
    /// </summary>
    Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
        Guid spacePublicId,
        long userId,
        CancellationToken cancellationToken = default);
}
