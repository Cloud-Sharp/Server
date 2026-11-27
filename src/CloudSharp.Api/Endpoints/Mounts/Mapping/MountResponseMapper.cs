using CloudSharp.Api.Endpoints.Mounts.Responses;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.UseCases.Mounts.Dtos;

namespace CloudSharp.Api.Endpoints.Mounts.Mapping;

internal static class MountResponseMapper
{
    internal static MountResponse ToResponse(MountDto mount) =>
        new(mount.SpacePublicId, mount.MountPublicId, mount.Name, mount.Visibility, mount.Status,
            ToResponse(mount.MyAccess), mount.CreatedAt, mount.UpdatedAt, mount.ArchivedAt);

    internal static MountPageResponse ToResponse(MountPageDto page, string? nextCursor) =>
        new(page.Items.Select(mount => new MountSummaryResponse(
            mount.MountPublicId, mount.Name, mount.Visibility, mount.Status,
            mount.CreatedAt, mount.UpdatedAt, ToResponse(mount.MyAccess))).ToArray(), nextCursor, page.CanCreateMount);

    internal static MountPermissionOverwriteResponse ToResponse(MountPermissionOverwriteDto overwrite) =>
        new(overwrite.TargetType, overwrite.TargetPublicId, ToCodes(overwrite.Allow), ToCodes(overwrite.Deny));

    internal static MountPermissionPageResponse ToResponse(MountPermissionPageDto page, string? nextCursor) =>
        new(page.Items.Select(ToResponse).ToArray(), nextCursor);

    internal static MountPermissionPreviewPageResponse ToResponse(
        MountPermissionPreviewPageDto page,
        string? nextCursor) =>
        new(page.Items.Select(item => new MountPermissionPreviewResponse(
            item.UserPublicId,
            ToCodes(item.CurrentPermissions),
            ToCodes(item.PreviewPermissions),
            item.CurrentSources.Select(ToResponse).ToArray(),
            item.PreviewSources.Select(ToResponse).ToArray())).ToArray(), nextCursor);

    private static MountAccessResponse ToResponse(MountAccessDto access) =>
        new(ToCodes(access.Permissions), access.CanManage);

    private static MountPermissionSourceResponse ToResponse(MountPermissionSourceDto source) =>
        new(source.SourceType, ToCodes(source.Allow), ToCodes(source.Deny), source.RolePublicId);

    private static string[] ToCodes(IEnumerable<SpacePermission> permissions) =>
        permissions.Order().Select(SpacePermissionCodes.Get).ToArray();
}
