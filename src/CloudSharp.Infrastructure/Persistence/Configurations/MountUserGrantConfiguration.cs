using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class MountUserGrantConfiguration : IEntityTypeConfiguration<MountUserGrantEntity>
{
    public void Configure(EntityTypeBuilder<MountUserGrantEntity> builder)
    {
        builder.ToTable("mount_user_grants", table =>
            table.HasCheckConstraint(
                "chk_mount_user_grants_overwrite_masks",
                MountOverwriteMask.CheckConstraintSql));

        builder.HasKey(grant => new { grant.MountId, grant.UserId }).HasName("pk_mount_user_grants");
        builder.Property(grant => grant.MountId).HasColumnName("mount_id");
        builder.Property(grant => grant.UserId).HasColumnName("user_id");
        builder.Property(grant => grant.AllowMask).HasColumnName("allow_mask");
        builder.Property(grant => grant.DenyMask).HasColumnName("deny_mask");

        builder.HasOne(grant => grant.Mount)
            .WithMany()
            .HasForeignKey(grant => grant.MountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_mount_user_grants_mount");
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(grant => grant.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mount_user_grants_user");
        builder.HasIndex(grant => new { grant.UserId, grant.MountId })
            .HasDatabaseName("idx_mount_user_grants_user_mount");
    }
}
