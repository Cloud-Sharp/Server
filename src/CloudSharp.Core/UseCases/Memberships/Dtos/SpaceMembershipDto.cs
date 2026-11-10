using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceMembershipDto(
    Guid MembershipPublicId,
    SpaceMembershipUserDto User,
    SpaceMembershipStatus Status,
    IReadOnlyList<SpaceMembershipRoleDto> Roles,
    DateTimeOffset JoinedAt,
    DateTimeOffset UpdatedAt);
