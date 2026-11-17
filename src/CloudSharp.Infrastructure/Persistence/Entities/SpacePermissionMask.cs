using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Infrastructure.Persistence.Entities;

internal static class SpacePermissionMask
{
    // These bit positions are part of the database format and must remain fixed.
    private static readonly IReadOnlyDictionary<SpacePermission, long> Bits =
        new Dictionary<SpacePermission, long>
        {
            [SpacePermission.SpaceView] = 1L << 0,
            [SpacePermission.SpaceUpdate] = 1L << 1,
            [SpacePermission.FilesRead] = 1L << 2,
            [SpacePermission.FilesWrite] = 1L << 3,
            [SpacePermission.FilesDownload] = 1L << 4,
            [SpacePermission.SharesView] = 1L << 5,
            [SpacePermission.SharesManage] = 1L << 6,
            [SpacePermission.TagsManage] = 1L << 7,
            [SpacePermission.InvitesManage] = 1L << 8,
            [SpacePermission.MembersView] = 1L << 9,
            [SpacePermission.MembersManage] = 1L << 10,
            [SpacePermission.UploadsManage] = 1L << 11,
            [SpacePermission.OperationsManage] = 1L << 12,
        };

    public static long KnownBits { get; } = Bits.Values.Aggregate(0L, static (mask, bit) => mask | bit);

    public static long Encode(IReadOnlySet<SpacePermission> permissions)
    {
        var mask = 0L;
        foreach (var permission in permissions)
        {
            if (!Bits.TryGetValue(permission, out var bit))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(permissions), permission, "Space permission has no assigned database bit.");
            }

            mask |= bit;
        }

        return mask;
    }

    public static SpacePermission[] Decode(long mask)
    {
        if ((mask & ~KnownBits) != 0)
        {
            throw new InvalidOperationException("Stored Space permission mask contains undefined bits.");
        }

        return Bits.Where(entry => (mask & entry.Value) != 0).Select(entry => entry.Key).ToArray();
    }
}
