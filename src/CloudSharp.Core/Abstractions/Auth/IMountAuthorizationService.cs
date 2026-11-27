using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Auth;

/// <summary>인증된 actor의 현재 Space 접근 자격과 Mount 유효 권한을 평가한다.</summary>
public interface IMountAuthorizationService
{
    /// <summary>
    /// 요청 시점의 Space snapshot과 Mount를 조회해 불변 context를 반환한다. HTTP 마스킹은 호출 경계의
    /// 책임이며 현재 구현은 Session만 지원한다.
    /// </summary>
    Task<Result<AuthorizedMountContext>> AuthorizeAsync(
        MountAuthorizationQuery query,
        CancellationToken cancellationToken = default);
}
