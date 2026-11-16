using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceConfiguration : IEntityTypeConfiguration<SpaceEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceEntity> builder)
    {
        builder.ToTable(
            "spaces",
            table =>
            {
                table.HasCheckConstraint(
                    "chk_spaces_name_not_blank",
                    "btrim(name) <> ''");
                table.HasCheckConstraint(
                    "chk_spaces_quota_non_negative",
                    "(storage_allowed_bytes IS NULL OR storage_allowed_bytes >= 0) AND storage_used_bytes >= 0 AND storage_reserved_bytes >= 0");
                table.HasCheckConstraint(
                    "chk_spaces_quota_capacity",
                    "storage_used_bytes::numeric + storage_reserved_bytes <= coalesce(storage_allowed_bytes, 9223372036854775807)");
                table.HasCheckConstraint(
                    "chk_spaces_version_positive",
                    "version > 0");
                table.HasCheckConstraint(
                    "chk_spaces_status_timestamps",
                    """
                    (status = 'ARCHIVED') = (archived_at IS NOT NULL)
                    AND (status IN ('DELETING', 'DELETED')) = (deleting_at IS NOT NULL)
                    AND (status = 'DELETED') = (deleted_at IS NOT NULL)
                    AND (status <> 'DELETED' OR deleted_at >= deleting_at)
                    """);
            });

        builder.HasKey(entity => entity.Id).HasName("pk_spaces");
        builder.Property(entity => entity.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(entity => entity.PublicId).HasColumnName("public_id").HasColumnType("uuid");
        builder.Property(entity => entity.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .HasColumnType("space_status_t")
            .HasDefaultValue(SpaceStatus.Active)
            .ValueGeneratedNever();
        builder.Property(entity => entity.AllowedBytes).HasColumnName("storage_allowed_bytes");
        builder.Property(entity => entity.UsedBytes)
            .HasColumnName("storage_used_bytes")
            .HasDefaultValue(0L)
            .ValueGeneratedNever();
        builder.Property(entity => entity.ReservedBytes)
            .HasColumnName("storage_reserved_bytes")
            .HasDefaultValue(0L)
            .ValueGeneratedNever();
        builder.Property(entity => entity.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.ArchivedAt).HasColumnName("archived_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.DeletingAt).HasColumnName("deleting_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_spaces_owner_user");

        builder.HasIndex(entity => entity.PublicId)
            .HasDatabaseName("ux_spaces_public_id")
            .IsUnique();
        builder.HasIndex(entity => new { entity.OwnerUserId, entity.Status })
            .HasDatabaseName("idx_spaces_owner_status");
        builder.HasIndex(entity => new { entity.Status, entity.CreatedAt, entity.Id })
            .HasDatabaseName("idx_spaces_status_created")
            .IsDescending(false, true, true);
    }
}
