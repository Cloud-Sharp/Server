using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for an explicit grant directly to a User.</summary>
public sealed class MountUserGrantEntity
{
    public long MountId { get; set; }
    public long UserId { get; set; }
    public long PermissionMask { get; set; }
    public MountEntity Mount { get; set; } = null!;

    public MountAccessGrant ToDomain() =>
        MountAccessGrant.Reconstitute(MountGrantTargetType.User, UserId, MountPermissionMask.Decode(PermissionMask));

    public static MountUserGrantEntity FromDomain(MountEntity mount, MountAccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(grant);

        var entity = new MountUserGrantEntity
        {
            MountId = mount.Id,
            UserId = grant.TargetId,
            Mount = mount,
        };
        entity.UpdateFrom(grant);
        return entity;
    }

    public void UpdateFrom(MountAccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (grant.TargetType != MountGrantTargetType.User || grant.TargetId != UserId)
        {
            throw new ArgumentException("The grant must target this row's User.", nameof(grant));
        }

        PermissionMask = MountPermissionMask.Encode(grant.Permissions);
    }
}
