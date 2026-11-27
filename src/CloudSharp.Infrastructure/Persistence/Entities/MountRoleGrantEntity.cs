using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Mount permission overwrite for a Role in the Mount's Space.</summary>
public sealed class MountRoleGrantEntity
{
    public long MountId { get; set; }
    public long SpaceId { get; set; }
    public long RoleId { get; set; }
    public long AllowMask { get; set; }
    public long DenyMask { get; set; }
    public MountEntity Mount { get; set; } = null!;

    public MountPermissionOverwrite ToDomain() =>
        MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.Role,
            RoleId,
            MountOverwriteMask.Decode(AllowMask),
            MountOverwriteMask.Decode(DenyMask));

    public static MountRoleGrantEntity FromDomain(MountEntity mount, MountPermissionOverwrite overwrite)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(overwrite);

        var entity = new MountRoleGrantEntity
        {
            MountId = mount.Id,
            SpaceId = mount.SpaceId,
            RoleId = overwrite.TargetId,
            Mount = mount,
        };
        entity.UpdateFrom(overwrite);
        return entity;
    }

    public void UpdateFrom(MountPermissionOverwrite overwrite)
    {
        ArgumentNullException.ThrowIfNull(overwrite);

        if (overwrite.TargetType != MountOverwriteTargetType.Role || overwrite.TargetId != RoleId)
        {
            throw new ArgumentException("The overwrite must target this row's Role.", nameof(overwrite));
        }

        if (overwrite.IsEmpty)
        {
            throw new ArgumentException(
                "A stored Mount overwrite must allow or deny at least one permission.", nameof(overwrite));
        }

        AllowMask = MountOverwriteMask.Encode(overwrite.Allow);
        DenyMask = MountOverwriteMask.Encode(overwrite.Deny);
    }
}
