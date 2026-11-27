using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountPermissionPreviewPageDto(
    IReadOnlyList<MountPermissionPreviewDto> Items,
    string? NextCursor);

public sealed record MountPermissionPreviewDto(
    Guid UserPublicId,
    IReadOnlySet<SpacePermission> CurrentPermissions,
    IReadOnlySet<SpacePermission> PreviewPermissions,
    IReadOnlyList<MountPermissionSourceDto> CurrentSources,
    IReadOnlyList<MountPermissionSourceDto> PreviewSources);

/// <summary>계산에 적용된 근거다. RolePublicId는 SourceType이 Role인 경우에만 값이 있다.</summary>
public sealed record MountPermissionSourceDto(
    MountPermissionSourceType SourceType,
    IReadOnlySet<SpacePermission> Allow,
    IReadOnlySet<SpacePermission> Deny,
    Guid? RolePublicId)
{
    public static MountPermissionSourceDto From(MountPermissionSource source) =>
        new(source.SourceType, source.Allow, source.Deny, source.RolePublicId);
}
