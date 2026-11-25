using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.Abstractions.Auth;

public interface ISpaceListAccessPolicy
{
    /// <summary>목록에 표시할 수 있는 항목의 인가 결과를 반환한다.</summary>
    AuthorizedSpaceContext? Evaluate(CurrentActor actor, SpaceAuthorizationSnapshot snapshot);
}
