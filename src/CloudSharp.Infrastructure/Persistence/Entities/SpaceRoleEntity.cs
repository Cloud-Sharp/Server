using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Space role. Permission links are stored separately.</summary>
public sealed class SpaceRoleEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long SpaceId { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public SpaceRoleSystemKey? SystemKey { get; set; }
    public string? Color { get; set; }
    public int Position { get; set; }
    public bool IsManaged { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Restores the role using the permissions from its stored links.</summary>
    public SpaceRole ToDomain(IEnumerable<SpacePermission> permissions) =>
        SpaceRole.Reconstitute(
            id: Id,
            publicId: PublicId,
            spaceId: SpaceId,
            name: Name,
            normalizedName: NormalizedName,
            systemKey: SystemKey,
            color: Color,
            position: Position,
            isManaged: IsManaged,
            version: Version,
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            deletedAt: DeletedAt,
            permissions: permissions);

    public static SpaceRoleEntity FromDomain(SpaceRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        var entity = new SpaceRoleEntity { Id = role.Id };
        entity.UpdateFrom(role);
        return entity;
    }

    public void UpdateFrom(SpaceRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        PublicId = role.PublicId;
        SpaceId = role.SpaceId;
        Name = role.Name;
        NormalizedName = role.NormalizedName;
        SystemKey = role.SystemKey;
        Color = role.Color;
        Position = role.Position;
        IsManaged = role.IsManaged;
        Version = role.Version;
        CreatedAt = role.CreatedAt;
        UpdatedAt = role.UpdatedAt;
        DeletedAt = role.DeletedAt;
    }
}
