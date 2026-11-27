using System.Collections.Frozen;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// Mount 인가에 성공한 요청 범위의 불변 결과. <see cref="EffectivePermissions"/>는 덮어쓰기와 암묵적 권한을
/// 적용한 Mount 범위 권한이며 항상 <see cref="SpacePermission.MountView"/>를 포함한다.
/// 다른 요청에 캐시하거나 리소스의 버전·상태 검증을 대체하지 않는다.
/// </summary>
public sealed class AuthorizedMountContext
{
    public AuthorizedSpaceContext Space { get; }
    public long MountId { get; }
    public Guid MountPublicId { get; }
    public IReadOnlySet<SpacePermission> EffectivePermissions { get; }

    internal AuthorizedMountContext(
        AuthorizedSpaceContext space,
        long mountId,
        Guid mountPublicId,
        IEnumerable<SpacePermission> effectivePermissions)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(effectivePermissions);
        Space = space;
        MountId = mountId;
        MountPublicId = mountPublicId;
        EffectivePermissions = effectivePermissions.ToFrozenSet();
    }
}
