using CloudSharp.Core.Domain.Invites;

namespace CloudSharp.Core.UseCases.Invites.Dtos;

public sealed record SpaceInviteDto(
    Guid InvitePublicId,
    SpaceInviteStatus Status,
    DateTimeOffset ExpiresAt,
    int? MaxUses,
    int UseCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RevokedAt)
{
    internal static SpaceInviteDto FromInvite(SpaceInvite invite) => new(
        invite.PublicId,
        invite.Status,
        invite.ExpiresAt,
        invite.MaxUses,
        invite.UseCount,
        invite.CreatedAt,
        invite.UpdatedAt,
        invite.RevokedAt);
}
