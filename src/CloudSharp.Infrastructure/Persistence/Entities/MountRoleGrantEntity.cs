using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for an explicit grant to a Role in the Mount's Space.</summary>
public sealed class MountRoleGrantEntity
{
    public long MountId { get; set; }
    public long SpaceId { get; set; }
    public long RoleId { get; set; }
    public long PermissionMask { get; set; }
    public MountEntity Mount { get; set; } = null!;

    public MountAccessGrant ToDomain() =>
        MountAccessGrant.Reconstitute(MountGrantTargetType.Role, RoleId, MountPermissionMask.Decode(PermissionMask));

    public static MountRoleGrantEntity FromDomain(MountEntity mount, MountAccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(grant);

        var entity = new MountRoleGrantEntity
        {
            MountId = mount.Id,
            SpaceId = mount.SpaceId,
            RoleId = grant.TargetId,
            Mount = mount,
        };
        entity.UpdateFrom(grant);
        return entity;
    }

    public void UpdateFrom(MountAccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (grant.TargetType != MountGrantTargetType.Role || grant.TargetId != RoleId)
        {
            throw new ArgumentException("The grant must target this row's Role.", nameof(grant));
        }

        PermissionMask = MountPermissionMask.Encode(grant.Permissions);
    }
}
