using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class MountRoleGrantConfiguration : IEntityTypeConfiguration<MountRoleGrantEntity>
{
    public void Configure(EntityTypeBuilder<MountRoleGrantEntity> builder)
    {
        builder.ToTable("mount_role_grants", table =>
            table.HasCheckConstraint(
                "chk_mount_role_grants_permission_mask",
                FormattableString.Invariant(
                    $"permission_mask > 0 AND (permission_mask & {MountPermissionMask.KnownBits}::bigint) = permission_mask")));

        builder.HasKey(grant => new { grant.MountId, grant.RoleId }).HasName("pk_mount_role_grants");
        builder.Property(grant => grant.MountId).HasColumnName("mount_id");
        builder.Property(grant => grant.SpaceId).HasColumnName("space_id");
        builder.Property(grant => grant.RoleId).HasColumnName("role_id");
        builder.Property(grant => grant.PermissionMask).HasColumnName("permission_mask");

        builder.HasOne(grant => grant.Mount)
            .WithMany()
            .HasForeignKey(grant => new { grant.MountId, grant.SpaceId })
            .HasPrincipalKey(mount => new { mount.Id, mount.SpaceId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_mount_role_grants_mount");
        builder.HasOne<SpaceRoleEntity>()
            .WithMany()
            .HasForeignKey(grant => new { grant.RoleId, grant.SpaceId })
            .HasPrincipalKey(role => new { role.Id, role.SpaceId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_mount_role_grants_role");
        builder.HasIndex(grant => new { grant.RoleId, grant.MountId })
            .HasDatabaseName("idx_mount_role_grants_role_mount");
    }
}
