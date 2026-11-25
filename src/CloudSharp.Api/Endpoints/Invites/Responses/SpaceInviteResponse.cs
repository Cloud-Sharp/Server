using CloudSharp.Core.Domain.Invites;

namespace CloudSharp.Api.Endpoints.Invites.Responses;

public sealed record SpaceInviteResponse(
    Guid InvitePublicId,
    SpaceInviteStatus Status,
    DateTimeOffset ExpiresAt,
    int? MaxUses,
    int UseCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RevokedAt);
