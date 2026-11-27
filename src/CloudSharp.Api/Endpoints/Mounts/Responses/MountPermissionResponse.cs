using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Api.Endpoints.Mounts.Responses;

internal sealed record MountPermissionOverwriteResponse(
    MountOverwriteTargetType TargetType,
    Guid TargetPublicId,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny);

internal sealed record MountPermissionPageResponse(
    IReadOnlyList<MountPermissionOverwriteResponse> Items,
    string? NextCursor);

internal sealed record MountPermissionPreviewPageResponse(
    IReadOnlyList<MountPermissionPreviewResponse> Items,
    string? NextCursor);

internal sealed record MountPermissionPreviewResponse(
    Guid UserPublicId,
    IReadOnlyList<string> CurrentPermissions,
    IReadOnlyList<string> PreviewPermissions,
    IReadOnlyList<MountPermissionSourceResponse> CurrentSources,
    IReadOnlyList<MountPermissionSourceResponse> PreviewSources);

internal sealed record MountPermissionSourceResponse(
    MountPermissionSourceType SourceType,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    Guid? RolePublicId);
