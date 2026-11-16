using CloudSharp.Core.Domain.Invites;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Space invite and its token hash.</summary>
public sealed class SpaceInviteEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long SpaceId { get; set; }
    public long CreatedByUserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public SpaceInviteStatus Status { get; set; }
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public SpaceInvite ToDomain() =>
        SpaceInvite.Reconstitute(
            id: Id,
            publicId: PublicId,
            spaceId: SpaceId,
            createdByUserId: CreatedByUserId,
            tokenHash: CloudSharp.Core.Common.Tokens.TokenHash.Reconstitute(TokenHash),
            status: Status,
            useCount: UseCount,
            maxUses: MaxUses,
            expiresAt: ExpiresAt,
            version: Version,
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            revokedAt: RevokedAt);

    public static SpaceInviteEntity FromDomain(SpaceInvite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);

        var entity = new SpaceInviteEntity { Id = invite.Id };
        entity.UpdateFrom(invite);
        return entity;
    }

    public void UpdateFrom(SpaceInvite invite)
    {
        ArgumentNullException.ThrowIfNull(invite);

        PublicId = invite.PublicId;
        SpaceId = invite.SpaceId;
        CreatedByUserId = invite.CreatedByUserId;
        TokenHash = invite.TokenHash.Value;
        Status = invite.Status;
        UseCount = invite.UseCount;
        MaxUses = invite.MaxUses;
        ExpiresAt = invite.ExpiresAt;
        Version = invite.Version;
        CreatedAt = invite.CreatedAt;
        UpdatedAt = invite.UpdatedAt;
        RevokedAt = invite.RevokedAt;
    }
}
