using System.Collections.Frozen;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// 인가에 성공한 요청 범위의 불변 결과. 다른 요청에 캐시하거나 리소스의 소유권·상태 검증을 대체하지 않는다.
/// </summary>
public sealed class AuthorizedSpaceContext
{
    public long SpaceId { get; }
    public Guid SpacePublicId { get; }
    public ActorType ActorType { get; }
    public long UserId { get; }
    public long? MembershipId { get; }
    public IReadOnlySet<SpacePermission> EffectivePermissions { get; }
    public SpaceAuthorizationSource AuthorizationSource { get; }

    internal AuthorizedSpaceContext(
        long spaceId,
        Guid spacePublicId,
        ActorType actorType,
        long userId,
        long? membershipId,
        IEnumerable<SpacePermission> effectivePermissions,
        SpaceAuthorizationSource authorizationSource)
    {
        SpaceId = spaceId;
        SpacePublicId = spacePublicId;
        ActorType = actorType;
        UserId = userId;
        MembershipId = membershipId;
        EffectivePermissions = effectivePermissions.ToFrozenSet();
        AuthorizationSource = authorizationSource;
    }
}
