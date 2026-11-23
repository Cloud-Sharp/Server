using CloudSharp.Api.Endpoints.Spaces.Responses;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;

namespace CloudSharp.Api.Endpoints.Spaces.Mapping;

internal static class SpaceResponseMapper
{
    internal static SpaceResponse ToResponse(SpaceDto space) =>
        new(
            space.SpacePublicId,
            space.OwnerUserPublicId,
            space.Name,
            space.Description,
            FormatStatus(space.Status),
            new SpaceAccessResponse(
                FormatSource(space.MyAccess.Source),
                space.MyAccess.Permissions
                    .OrderBy(permission => permission)
                    .Select(SpacePermissionCodes.Get)
                    .ToArray()),
            space.CreatedAt,
            space.UpdatedAt);

    internal static SpacePageResponse ToResponse(SpacePageDto page) =>
        new(page.Items.Select(ToResponse).ToArray(), page.NextCursor);

    internal static SpaceQuotaResponse ToResponse(SpaceQuotaDto quota) =>
        new(
            quota.SpacePublicId,
            quota.AllowedBytes,
            quota.UsedBytes,
            quota.ReservedBytes,
            quota.AvailableBytes);

    internal static SpaceDeletionAcceptedResponse ToResponse(SpaceDeletionAcceptedDto deletion) =>
        new(deletion.SpacePublicId, FormatStatus(deletion.Status), deletion.AcceptedAt);

    private static string FormatStatus(SpaceStatus status) => status switch
    {
        SpaceStatus.Active => "ACTIVE",
        SpaceStatus.Archived => "ARCHIVED",
        SpaceStatus.Deleting => "DELETING",
        SpaceStatus.Deleted => "DELETED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported space status."),
    };

    private static string FormatSource(SpaceAuthorizationSource source) => source switch
    {
        SpaceAuthorizationSource.SystemAdminBypass => "SYSTEM_ADMIN_BYPASS",
        SpaceAuthorizationSource.Owner => "OWNER",
        SpaceAuthorizationSource.Membership => "MEMBERSHIP",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported space authorization source."),
    };
}
