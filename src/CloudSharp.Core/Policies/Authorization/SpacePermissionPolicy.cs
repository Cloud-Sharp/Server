using System.Collections.Frozen;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// 호출자가 조회한 Space·사용자·Membership·현재 Role snapshot으로 유효 Permission 집합을 계산하는 순수 정책.
/// DB 조회, Space 상태 검사, Session SystemRole 관리자 우회, MCP scope·allowlist 제한, HTTP 응답 결정은
/// 후속 ISpaceAuthorizationService의 책임이다.
/// </summary>
public static class SpacePermissionPolicy
{
    /// <summary>
    /// 사용자의 유효 Permission을 계산한다. OWNER는 Membership 없이도 현재 정의된 모든
    /// <see cref="SpacePermission"/>을 갖는다. 그 외 사용자는 해당 사용자·해당 Space의 ACTIVE Membership에
    /// 할당된 동일 Space의 비삭제 Role Permission 합집합만 갖는다. SUSPENDED·LEFT이거나 관계가 다르면
    /// 빈 집합이다. Role 이름·SystemKey는 보지 않고 <see cref="SpaceRole.Permissions"/>만 사용한다.
    /// 결과는 매 호출 복사한 FrozenSet이며 사용자별로 캐시하지 않는다.
    /// </summary>
    public static IReadOnlySet<SpacePermission> Evaluate(
        Space space,
        long userId,
        SpaceMembership? membership,
        IReadOnlyCollection<SpaceRole> roles)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(roles);

        var rolesById = IndexRolesById(roles);

        if (userId <= 0 || space.Id <= 0)
        {
            return FrozenSet<SpacePermission>.Empty;
        }

        if (space.OwnerUserId == userId)
        {
            return Enum.GetValues<SpacePermission>().ToFrozenSet();
        }

        if (membership is null
            || membership.SpaceId != space.Id
            || membership.UserId != userId
            || membership.Status != SpaceMembershipStatus.Active)
        {
            return FrozenSet<SpacePermission>.Empty;
        }

        var effective = new HashSet<SpacePermission>();
        foreach (var assignment in membership.RoleAssignments)
        {
            if (rolesById.TryGetValue(assignment.RoleId, out var role)
                && role.SpaceId == space.Id
                && role.DeletedAt is null)
            {
                effective.UnionWith(role.Permissions);
            }
        }

        return effective.ToFrozenSet();
    }

    /// <summary>
    /// 요구 Permission을 전부 포함하는지 검사한다. 미정의 enum 값은 없는 권한으로 취급한다.
    /// 빈 요구 집합은 true다. Space 접근 자격과 상태 확인은 호출 서비스가 별도로 수행한다.
    /// </summary>
    public static bool HasAll(
        IReadOnlySet<SpacePermission> effectivePermissions,
        IEnumerable<SpacePermission> requiredPermissions)
    {
        ArgumentNullException.ThrowIfNull(effectivePermissions);
        ArgumentNullException.ThrowIfNull(requiredPermissions);

        foreach (var required in requiredPermissions)
        {
            if (!Enum.IsDefined(required) || !effectivePermissions.Contains(required))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<long, SpaceRole> IndexRolesById(IReadOnlyCollection<SpaceRole> roles)
    {
        var rolesById = new Dictionary<long, SpaceRole>();
        foreach (var role in roles)
        {
            if (role is null)
            {
                throw new ArgumentException("Roles must not contain null elements.", nameof(roles));
            }

            // 미저장 Role(Id=0)은 어떤 할당과도 연결될 수 없으므로 인덱스에서 제외한다.
            if (role.Id <= 0)
            {
                continue;
            }

            if (!rolesById.TryAdd(role.Id, role))
            {
                throw new ArgumentException("Roles must not repeat a persisted role id.", nameof(roles));
            }
        }

        return rolesById;
    }
}
