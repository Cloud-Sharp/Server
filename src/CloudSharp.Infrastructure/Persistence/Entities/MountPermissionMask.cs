using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

internal static class MountPermissionMask
{
    // These bit positions are part of the database format and must remain fixed.
    private static readonly IReadOnlyDictionary<MountPermission, long> Bits =
        new Dictionary<MountPermission, long>
        {
            [MountPermission.FilesRead] = 1L << 0,
            [MountPermission.FilesWrite] = 1L << 1,
            [MountPermission.FilesDownload] = 1L << 2,
        };

    public static long KnownBits { get; } = Bits.Values.Aggregate(0L, static (mask, bit) => mask | bit);

    public static long Encode(IReadOnlySet<MountPermission> permissions)
    {
        var mask = 0L;
        foreach (var permission in permissions)
        {
            if (!Bits.TryGetValue(permission, out var bit))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(permissions), permission, "Mount permission has no assigned database bit.");
            }

            mask |= bit;
        }

        if (mask == 0)
        {
            throw new ArgumentException("A stored Mount grant must contain at least one permission.", nameof(permissions));
        }

        return mask;
    }

    public static MountPermission[] Decode(long mask)
    {
        if (mask == 0 || (mask & ~KnownBits) != 0)
        {
            throw new InvalidOperationException("Stored Mount permission mask is empty or contains undefined bits.");
        }

        return Bits.Where(entry => (mask & entry.Value) != 0).Select(entry => entry.Key).ToArray();
    }
}
