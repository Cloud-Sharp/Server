using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>요청자의 Mount 범위 유효 권한과 현재 Mount 설정·상태 변경 자격이다.</summary>
public sealed record MountAccessDto(IReadOnlySet<SpacePermission> Permissions, bool CanManage)
{
    public static MountAccessDto From(IReadOnlySet<SpacePermission> permissions) =>
        new(permissions, permissions.Contains(SpacePermission.MountManage));
}
