using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Mount. Its grants are stored separately.</summary>
public sealed class MountEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long SpaceId { get; set; }
    public string Name { get; set; } = null!;
    public MountVisibility Visibility { get; set; }
    public MountStatus Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Restores the Mount and its materialized grant rows.</summary>
    public Mount ToDomain(
        IEnumerable<MountRoleGrantEntity> roleGrants,
        IEnumerable<MountUserGrantEntity> userGrants)
    {
        ArgumentNullException.ThrowIfNull(roleGrants);
        ArgumentNullException.ThrowIfNull(userGrants);

        return Mount.Reconstitute(
            id: Id,
            publicId: PublicId,
            spaceId: SpaceId,
            name: Name,
            visibility: Visibility,
            status: Status,
            version: Version,
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            archivedAt: ArchivedAt,
            grants: roleGrants.Select(grant => grant.ToDomain())
                .Concat(userGrants.Select(grant => grant.ToDomain())));
    }

    public static MountEntity FromDomain(Mount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        var entity = new MountEntity { Id = mount.Id };
        entity.UpdateFrom(mount);
        return entity;
    }

    public void UpdateFrom(Mount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        PublicId = mount.PublicId;
        SpaceId = mount.SpaceId;
        Name = mount.Name;
        Visibility = mount.Visibility;
        Status = mount.Status;
        Version = mount.Version;
        CreatedAt = mount.CreatedAt;
        UpdatedAt = mount.UpdatedAt;
        ArchivedAt = mount.ArchivedAt;
    }
}
