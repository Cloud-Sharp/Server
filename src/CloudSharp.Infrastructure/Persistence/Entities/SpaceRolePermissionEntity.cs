namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row linking a Space role to a catalog permission.</summary>
public sealed class SpaceRolePermissionEntity
{
    public long RoleId { get; set; }
    public long PermissionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SpaceRoleEntity Role { get; set; } = null!;
}
