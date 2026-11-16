using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceMembershipConfiguration : IEntityTypeConfiguration<SpaceMembershipEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceMembershipEntity> builder)
    {
        builder.ToTable(
            "space_memberships",
            table =>
            {
                table.HasCheckConstraint("chk_space_memberships_version_positive", "version > 0");
                table.HasCheckConstraint(
                    "chk_space_memberships_status_timestamps",
                    "(status = 'SUSPENDED') = (suspended_at IS NOT NULL) AND (status = 'LEFT') = (left_at IS NOT NULL)");
            });

        builder.HasKey(entity => entity.Id).HasName("pk_space_memberships");
        builder.HasAlternateKey(entity => new { entity.Id, entity.SpaceId }).HasName("ak_space_memberships_id_space_id");
        builder.Property(entity => entity.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(entity => entity.PublicId).HasColumnName("public_id").HasColumnType("uuid");
        builder.Property(entity => entity.SpaceId).HasColumnName("space_id");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .HasColumnType("membership_status_t")
            .HasDefaultValue(SpaceMembershipStatus.Active)
            .ValueGeneratedNever();
        builder.Property(entity => entity.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.Property(entity => entity.JoinedAt).HasColumnName("joined_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.SuspendedAt).HasColumnName("suspended_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.LeftAt).HasColumnName("left_at").HasColumnType("timestamptz");

        builder.HasOne<SpaceEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.SpaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_memberships_space");
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_memberships_user");

        builder.HasIndex(entity => entity.PublicId)
            .HasDatabaseName("ux_space_memberships_public_id")
            .IsUnique();
        builder.HasIndex(entity => new { entity.SpaceId, entity.UserId }, "ux_space_memberships_current_space_user")
            .IsUnique()
            .HasFilter("status IN ('ACTIVE', 'SUSPENDED')");
        builder.HasIndex(entity => new { entity.SpaceId, entity.UserId }, "idx_space_memberships_space_user");
        builder.HasIndex(entity => new { entity.UserId, entity.Status, entity.Id })
            .HasDatabaseName("idx_space_memberships_user_status_id");
        builder.HasIndex(entity => new { entity.SpaceId, entity.Status, entity.Id })
            .HasDatabaseName("idx_space_memberships_space_status_id");
    }
}
