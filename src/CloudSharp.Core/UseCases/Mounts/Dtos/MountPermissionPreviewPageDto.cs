using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountPermissionPreviewPageDto(
    IReadOnlyList<MountPermissionPreviewDto> Items,
    string? NextCursor);

public sealed record MountPermissionPreviewDto(
    Guid UserPublicId,
    IReadOnlySet<MountPermission> CurrentPermissions,
    IReadOnlySet<MountPermission> PreviewPermissions,
    IReadOnlyList<MountPermissionSourceDto> CurrentSources,
    IReadOnlyList<MountPermissionSourceDto> PreviewSources);

/// <summary>RolePublicId는 SourceType이 Role인 경우에만 값이 있다.</summary>
public sealed record MountPermissionSourceDto(
    MountPermissionSourceType SourceType,
    IReadOnlySet<MountPermission> Permissions,
    Guid? RolePublicId);

public enum MountPermissionSourceType
{
    Default,
    Role,
    User,
}
