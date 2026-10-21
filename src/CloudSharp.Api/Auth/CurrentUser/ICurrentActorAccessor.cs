using CloudSharp.Core.Common.Actors;

namespace CloudSharp.Api.Auth.CurrentUser;

/// <summary>
/// 현재 요청의 Session 인증 결과를 보관한다. token hash는 비밀번호 변경처럼
/// 요청 범위 안에서만 필요한 handler에 제공하며 claims나 응답에는 넣지 않는다.
/// </summary>
public interface ICurrentActorAccessor
{
    CurrentActor? CurrentActor { get; }

    string? SessionTokenHash { get; }

    void Set(CurrentActor actor, string sessionTokenHash);
}
