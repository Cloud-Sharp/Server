using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>
/// Mount 권한 덮어쓰기의 allow_mask·deny_mask 형식이다. Space Role 권한과 같은 고정 비트를 사용하며
/// Mount 범위 권한의 비트만 허용한다.
/// </summary>
internal static class MountOverwriteMask
{
    public static long ScopedBits { get; } = SpacePermissionMask.Encode(MountScopedPermissions.All);

    /// <summary>두 mask 컬럼의 PostgreSQL CHECK 조건이다.</summary>
    public static string CheckConstraintSql { get; } = FormattableString.Invariant(
        $"(allow_mask | deny_mask) > 0 AND (allow_mask & deny_mask) = 0 AND ((allow_mask | deny_mask) & {ScopedBits}::bigint) = (allow_mask | deny_mask)");

    public static long Encode(IReadOnlySet<SpacePermission> permissions)
    {
        var mask = SpacePermissionMask.Encode(permissions);
        if ((mask & ~ScopedBits) != 0)
        {
            throw new ArgumentException("Mount overwrite permissions must be mount-scoped.", nameof(permissions));
        }

        return mask;
    }

    public static SpacePermission[] Decode(long mask)
    {
        if ((mask & ~ScopedBits) != 0)
        {
            throw new InvalidOperationException("Stored Mount overwrite mask contains bits outside the mount scope.");
        }

        return SpacePermissionMask.Decode(mask);
    }
}
