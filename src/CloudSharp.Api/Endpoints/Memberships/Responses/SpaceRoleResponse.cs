using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Api.Endpoints.Memberships.Responses;

public sealed record SpaceRoleResponse(
    Guid RolePublicId,
    SpaceRoleSystemKey Code,
    string Name,
    bool IsManaged,
    IReadOnlyList<string> Permissions);
