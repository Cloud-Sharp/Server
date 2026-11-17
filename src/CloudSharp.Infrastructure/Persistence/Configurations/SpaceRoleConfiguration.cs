using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceRoleConfiguration : IEntityTypeConfiguration<SpaceRoleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceRoleEntity> builder)
    {
        builder.ToTable(
            "space_roles",
            table =>
            {
                table.HasCheckConstraint("chk_space_roles_system_key", "system_key IN ('ADMIN', 'MEMBER', 'VIEWER')");
                table.HasCheckConstraint("chk_space_roles_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
                table.HasCheckConstraint("chk_space_roles_position_non_negative", "position >= 0");
                table.HasCheckConstraint("chk_space_roles_version_positive", "version > 0");
                table.HasCheckConstraint(
                    "chk_space_roles_permission_mask",
                    FormattableString.Invariant(
                        $"permission_mask >= 0 AND (permission_mask & {SpacePermissionMask.KnownBits}::BIGINT) = permission_mask"));
            });

        builder.HasKey(entity => entity.Id).HasName("pk_space_roles");
        builder.HasAlternateKey(entity => new { entity.Id, entity.SpaceId }).HasName("ak_space_roles_id_space_id");
        builder.Property(entity => entity.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(entity => entity.PublicId).HasColumnName("public_id").HasColumnType("uuid");
        builder.Property(entity => entity.SpaceId).HasColumnName("space_id");
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(entity => entity.SystemKey)
            .HasColumnName("system_key")
            .HasMaxLength(30)
            .HasConversion(new ValueConverter<SpaceRoleSystemKey, string>(
                key => ToStoreKey(key),
                key => FromStoreKey(key)));
        builder.Property(entity => entity.Color).HasColumnName("color").HasMaxLength(7).IsFixedLength();
        builder.Property(entity => entity.Position)
            .HasColumnName("position")
            .HasDefaultValue(0)
            .ValueGeneratedNever();
        builder.Property(entity => entity.IsManaged)
            .HasColumnName("is_managed")
            .HasDefaultValue(false)
            .ValueGeneratedNever();
        builder.Property(entity => entity.PermissionMask)
            .HasColumnName("permission_mask")
            .HasColumnType("bigint")
            .IsRequired()
            .HasDefaultValue(0L)
            .ValueGeneratedNever();
        builder.Property(entity => entity.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

        builder.HasOne<SpaceEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.SpaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_roles_space");

        builder.HasIndex(entity => entity.PublicId)
            .HasDatabaseName("ux_space_roles_public_id")
            .IsUnique();
        builder.HasIndex(entity => new { entity.SpaceId, entity.NormalizedName })
            .HasDatabaseName("ux_space_roles_active_normalized_name")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(entity => new { entity.SpaceId, entity.SystemKey })
            .HasDatabaseName("ux_space_roles_active_system_key")
            .IsUnique()
            .HasFilter("system_key IS NOT NULL AND deleted_at IS NULL");
        builder.HasIndex(entity => new { entity.SpaceId, entity.Position, entity.Id })
            .HasDatabaseName("idx_space_roles_space_position_id")
            .IsDescending(false, true, false);
        builder.HasIndex(entity => new { entity.SpaceId, entity.DeletedAt })
            .HasDatabaseName("idx_space_roles_space_deleted_at");
    }

    private static string ToStoreKey(SpaceRoleSystemKey key) => key switch
    {
        SpaceRoleSystemKey.Admin => "ADMIN",
        SpaceRoleSystemKey.Member => "MEMBER",
        SpaceRoleSystemKey.Viewer => "VIEWER",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported Space role system key."),
    };

    private static SpaceRoleSystemKey FromStoreKey(string key) => key switch
    {
        "ADMIN" => SpaceRoleSystemKey.Admin,
        "MEMBER" => SpaceRoleSystemKey.Member,
        "VIEWER" => SpaceRoleSystemKey.Viewer,
        _ => throw new InvalidOperationException("The Space role has an unsupported persisted system key."),
    };
}
