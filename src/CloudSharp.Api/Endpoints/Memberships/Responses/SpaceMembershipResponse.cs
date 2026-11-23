using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Api.Endpoints.Memberships.Responses;

public sealed record SpaceMembershipResponse(
    Guid MembershipPublicId,
    SpaceMembershipUserResponse User,
    SpaceMembershipStatus Status,
    IReadOnlyList<SpaceMembershipRoleResponse> Roles,
    DateTimeOffset JoinedAt,
    DateTimeOffset UpdatedAt);

public sealed record SpaceMembershipUserResponse(Guid UserPublicId, string? UserName, string? DisplayName);

public sealed record SpaceMembershipRoleResponse(Guid RolePublicId, SpaceRoleSystemKey Code, string Name);

public sealed record SpaceMembershipPageResponse(IReadOnlyList<SpaceMembershipResponse> Items, string? NextCursor);
