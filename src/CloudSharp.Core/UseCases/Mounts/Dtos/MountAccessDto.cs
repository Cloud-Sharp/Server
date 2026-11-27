using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>
/// 요청자가 현재 Space·Mount 상태에서 행사할 수 있는 Mount 범위 권한과 Mount 관리 자격이다.
/// 보관된 Space에서도 관리 조회는 가능하므로 <see cref="CanManage"/>는 상태와 무관하게 유효 MOUNT_MANAGE를 따른다.
/// </summary>
public sealed record MountAccessDto(IReadOnlySet<SpacePermission> Permissions, bool CanManage)
{
    public static MountAccessDto From(
        IReadOnlySet<SpacePermission> permissions,
        SpaceStatus spaceStatus,
        MountStatus mountStatus)
    {
        var exercisable = MountPermissionPolicy.ExercisablePermissions(permissions, spaceStatus, mountStatus);
        return new(exercisable, exercisable.Contains(SpacePermission.MountManage));
    }
}
