using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for a Mount permission overwrite for a User.</summary>
public sealed class MountUserGrantEntity
{
    public long MountId { get; set; }
    public long UserId { get; set; }
    public long AllowMask { get; set; }
    public long DenyMask { get; set; }
    public MountEntity Mount { get; set; } = null!;

    public MountPermissionOverwrite ToDomain() =>
        MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.User,
            UserId,
            MountOverwriteMask.Decode(AllowMask),
            MountOverwriteMask.Decode(DenyMask));

    public static MountUserGrantEntity FromDomain(MountEntity mount, MountPermissionOverwrite overwrite)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(overwrite);

        var entity = new MountUserGrantEntity
        {
            MountId = mount.Id,
            UserId = overwrite.TargetId,
            Mount = mount,
        };
        entity.UpdateFrom(overwrite);
        return entity;
    }

    public void UpdateFrom(MountPermissionOverwrite overwrite)
    {
        ArgumentNullException.ThrowIfNull(overwrite);

        if (overwrite.TargetType != MountOverwriteTargetType.User || overwrite.TargetId != UserId)
        {
            throw new ArgumentException("The overwrite must target this row's User.", nameof(overwrite));
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
