using CloudSharp.Api.Endpoints.Memberships.Responses;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.UseCases.Memberships.Dtos;

namespace CloudSharp.Api.Endpoints.Memberships.Mapping;

internal static class MembershipResponseMapper
{
    internal static SpaceMembershipResponse ToResponse(SpaceMembershipDto membership) => new(
        membership.MembershipPublicId,
        new SpaceMembershipUserResponse(
            membership.User.UserPublicId, membership.User.UserName, membership.User.DisplayName),
        membership.Status,
        membership.Roles.Select(role =>
            new SpaceMembershipRoleResponse(role.RolePublicId, role.Code, role.Name)).ToArray(),
        membership.JoinedAt,
        membership.UpdatedAt);

    internal static SpaceMembershipPageResponse ToResponse(SpaceMembershipPageDto page) =>
        new(page.Items.Select(ToResponse).ToArray(), page.NextCursor);

    internal static SpaceRoleResponse ToResponse(SpaceRoleDto role) => new(
        role.RolePublicId,
        role.Code,
        role.Name,
        role.IsManaged,
        role.Permissions.Order().Select(SpacePermissionCodes.Get).ToArray());
}
