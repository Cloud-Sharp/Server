using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class MountConfiguration : IEntityTypeConfiguration<MountEntity>
{
    public void Configure(EntityTypeBuilder<MountEntity> builder)
    {
        builder.ToTable("mounts", table =>
        {
            table.HasCheckConstraint("chk_mounts_name_not_blank", "btrim(name) <> ''");
            table.HasCheckConstraint("chk_mounts_version_positive", "version > 0");
            table.HasCheckConstraint("chk_mounts_archived_at", "(status = 'ARCHIVED') = (archived_at IS NOT NULL)");
        });

        builder.HasKey(mount => mount.Id).HasName("pk_mounts");
        builder.HasAlternateKey(mount => new { mount.Id, mount.SpaceId }).HasName("ak_mounts_id_space_id");
        builder.Property(mount => mount.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(mount => mount.PublicId).HasColumnName("public_id");
        builder.Property(mount => mount.SpaceId).HasColumnName("space_id");
        builder.Property(mount => mount.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(mount => mount.Visibility)
            .HasColumnName("visibility")
            .HasColumnType(PersistenceServiceCollectionExtensions.MountVisibilityEnumName);
        builder.Property(mount => mount.Status)
            .HasColumnName("status")
            .HasColumnType(PersistenceServiceCollectionExtensions.MountStatusEnumName)
            .HasDefaultValue(MountStatus.Active)
            .ValueGeneratedNever();
        builder.Property(mount => mount.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.Property(mount => mount.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(mount => mount.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(mount => mount.ArchivedAt).HasColumnName("archived_at").HasColumnType("timestamptz");

        builder.HasOne<SpaceEntity>()
            .WithMany()
            .HasForeignKey(mount => mount.SpaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mounts_space");
        builder.HasIndex(mount => mount.PublicId).IsUnique().HasDatabaseName("ux_mounts_public_id");
        builder.HasIndex(mount => new { mount.SpaceId, mount.Status, mount.Id })
            .HasDatabaseName("idx_mounts_space_status_id");
    }
}
