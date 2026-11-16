using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceMembershipRoleDto(
    Guid RolePublicId,
    SpaceRoleSystemKey Code,
    string Name);
