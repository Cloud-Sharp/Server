using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceRoleDto(
    Guid RolePublicId,
    SpaceRoleSystemKey Code,
    string Name,
    bool IsManaged,
    IReadOnlySet<SpacePermission> Permissions);
