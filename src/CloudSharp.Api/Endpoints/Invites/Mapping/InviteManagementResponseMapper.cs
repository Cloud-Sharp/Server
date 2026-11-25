using CloudSharp.Api.Endpoints.Invites.Responses;
using CloudSharp.Core.UseCases.Invites.Dtos;

namespace CloudSharp.Api.Endpoints.Invites.Mapping;

internal static class InviteManagementResponseMapper
{
    internal static SpaceInviteResponse ToResponse(SpaceInviteDto invite) => new(
        invite.InvitePublicId,
        invite.Status,
        invite.ExpiresAt,
        invite.MaxUses,
        invite.UseCount,
        invite.CreatedAt,
        invite.UpdatedAt,
        invite.RevokedAt);

    internal static SpaceInvitePageResponse ToResponse(SpaceInvitePageDto page) =>
        new(page.Items.Select(ToResponse).ToArray(), page.NextCursor);

    internal static CreateSpaceInviteResponse ToResponse(CreateSpaceInviteResultDto created) => new(
        created.InvitePublicId,
        created.InviteToken,
        created.InviteUrl,
        created.ExpiresAt,
        created.MaxUses);
}
