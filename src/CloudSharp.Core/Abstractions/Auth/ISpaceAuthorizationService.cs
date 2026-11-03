using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Auth;

/// <summary>인증된 actor의 현재 Space 접근 자격·상태·Permission을 평가한다.</summary>
public interface ISpaceAuthorizationService
{
    /// <summary>
    /// 요청 시점 권한을 조회해 불변 context를 반환한다. 리소스 관계 조건, HTTP 마스킹과 감사 저장은
    /// 호출 경계의 책임이다. 현재 구현은 Session만 지원한다.
    /// </summary>
    Task<Result<AuthorizedSpaceContext>> AuthorizeAsync(
        SpaceAuthorizationQuery query,
        CancellationToken cancellationToken = default);
}
