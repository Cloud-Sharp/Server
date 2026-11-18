using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Space and its storage counters.</summary>
public sealed class SpaceEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long OwnerUserId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public SpaceStatus Status { get; set; }
    public long? AllowedBytes { get; set; }
    public long UsedBytes { get; set; }
    public long ReservedBytes { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? DeletingAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public Space ToDomain() =>
        Space.Reconstitute(
            id: Id,
            publicId: PublicId,
            ownerUserId: OwnerUserId,
            name: Name,
            description: Description,
            status: Status,
            allowedBytes: AllowedBytes,
            usedBytes: UsedBytes,
            reservedBytes: ReservedBytes,
            version: Version,
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            archivedAt: ArchivedAt,
            deletingAt: DeletingAt,
            deletedAt: DeletedAt);

    public static SpaceEntity FromDomain(Space space)
    {
        ArgumentNullException.ThrowIfNull(space);

        var entity = new SpaceEntity { Id = space.Id };
        entity.UpdateFrom(space);
        return entity;
    }

    public void UpdateFrom(Space space)
    {
        ArgumentNullException.ThrowIfNull(space);

        PublicId = space.PublicId;
        OwnerUserId = space.OwnerUserId;
        Name = space.Name;
        Description = space.Description;
        Status = space.Status;
        AllowedBytes = space.AllowedBytes;
        UsedBytes = space.UsedBytes;
        ReservedBytes = space.ReservedBytes;
        Version = space.Version;
        CreatedAt = space.CreatedAt;
        UpdatedAt = space.UpdatedAt;
        ArchivedAt = space.ArchivedAt;
        DeletingAt = space.DeletingAt;
        DeletedAt = space.DeletedAt;
    }
}
