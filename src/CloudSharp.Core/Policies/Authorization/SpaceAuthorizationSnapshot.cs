using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Policies.Authorization;

/// <summary>
/// 동일 조회 범위에서 읽은 Space·Membership·Role snapshot. 도메인 객체는 reader가 독립적으로 복원하며
/// Role 컬렉션은 입력과 분리해 보관한다. 이 타입 자체는 접근 승인 결과가 아니다.
/// </summary>
public sealed class SpaceAuthorizationSnapshot
{
    public Space Space { get; }
    public SpaceMembership? Membership { get; }
    public IReadOnlyCollection<SpaceRole> Roles { get; }

    public SpaceAuthorizationSnapshot(
        Space space,
        SpaceMembership? membership,
        IEnumerable<SpaceRole> roles)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(roles);
        if (space.Id <= 0)
        {
            throw new ArgumentException("An authorization snapshot requires a persisted space.", nameof(space));
        }

        var copiedRoles = roles.ToArray();
        if (copiedRoles.Any(role => role is null))
        {
            throw new ArgumentException("Roles must not contain null elements.", nameof(roles));
        }

        Space = space;
        Membership = membership;
        Roles = Array.AsReadOnly(copiedRoles);
    }
}
