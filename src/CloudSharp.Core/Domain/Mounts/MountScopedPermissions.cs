using System.Collections.Frozen;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.Domain.Mounts;

/// <summary>
/// Mount마다 덮어쓸 수 있는 <see cref="SpacePermission"/> 집합이다. Space Role의 권한이 기본값이 되고,
/// Mount의 Role·사용자 덮어쓰기는 이 집합 안에서만 허용·거부할 수 있다.
/// </summary>
public static class MountScopedPermissions
{
    public static IReadOnlySet<SpacePermission> All { get; } = new HashSet<SpacePermission>
    {
        SpacePermission.MountView,
        SpacePermission.MountManage,
        SpacePermission.FilesRead,
        SpacePermission.FilesWrite,
        SpacePermission.FilesDownload,
    }.ToFrozenSet();

    public static bool Contains(SpacePermission permission) => All.Contains(permission);
}
