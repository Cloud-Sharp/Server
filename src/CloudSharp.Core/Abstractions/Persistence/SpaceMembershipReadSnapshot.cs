using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceMembershipReadSnapshot(
    SpaceMembership Membership,
    Guid UserPublicId,
    string? UserName,
    string? DisplayName,
    IReadOnlyList<SpaceRole> Roles);
