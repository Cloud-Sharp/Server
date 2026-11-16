using CloudSharp.Core.Domain.Invites;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceInviteConfiguration : IEntityTypeConfiguration<SpaceInviteEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceInviteEntity> builder)
    {
        builder.ToTable(
            "space_invites",
            table =>
            {
                table.HasCheckConstraint("chk_space_invites_use_count_non_negative", "use_count >= 0");
                table.HasCheckConstraint("chk_space_invites_max_uses_positive", "max_uses IS NULL OR max_uses > 0");
                table.HasCheckConstraint("chk_space_invites_max_uses_capacity", "max_uses IS NULL OR use_count <= max_uses");
                table.HasCheckConstraint("chk_space_invites_expiration", "expires_at > created_at");
                table.HasCheckConstraint("chk_space_invites_version_positive", "version > 0");
                table.HasCheckConstraint(
                    "chk_space_invites_status_timestamps",
                    "(status = 'REVOKED') = (revoked_at IS NOT NULL)");
            });

        builder.HasKey(entity => entity.Id).HasName("pk_space_invites");
        builder.Property(entity => entity.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(entity => entity.PublicId).HasColumnName("public_id").HasColumnType("uuid");
        builder.Property(entity => entity.SpaceId).HasColumnName("space_id");
        builder.Property(entity => entity.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(entity => entity.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .HasColumnType("invite_status_t")
            .HasDefaultValue(SpaceInviteStatus.Active)
            .ValueGeneratedNever();
        builder.Property(entity => entity.UseCount)
            .HasColumnName("use_count")
            .HasDefaultValue(0)
            .ValueGeneratedNever();
        builder.Property(entity => entity.MaxUses).HasColumnName("max_uses");
        builder.Property(entity => entity.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");

        builder.HasOne<SpaceEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.SpaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_invites_space");
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_invites_created_by_user");

        builder.HasIndex(entity => entity.PublicId)
            .HasDatabaseName("ux_space_invites_public_id")
            .IsUnique();
        builder.HasIndex(entity => entity.TokenHash)
            .HasDatabaseName("ux_space_invites_token_hash")
            .IsUnique();
        builder.HasIndex(entity => new { entity.SpaceId, entity.Status, entity.CreatedAt })
            .HasDatabaseName("idx_space_invites_space_status_created")
            .IsDescending(false, false, true);
        builder.HasIndex(entity => new { entity.Status, entity.ExpiresAt })
            .HasDatabaseName("idx_space_invites_status_expires");
    }
}
