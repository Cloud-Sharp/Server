using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for the Space permission catalog.</summary>
public sealed class PermissionEntity
{
    private static readonly IReadOnlyDictionary<string, SpacePermission> PermissionsByCode =
        Enum.GetValues<SpacePermission>().ToDictionary(SpacePermissionCodes.Get, StringComparer.Ordinal);

    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public SpacePermission ToDomain() =>
        PermissionsByCode.TryGetValue(Code, out var permission)
            ? permission
            : throw new InvalidOperationException($"Unsupported stored Space permission code '{Code}'.");
}
