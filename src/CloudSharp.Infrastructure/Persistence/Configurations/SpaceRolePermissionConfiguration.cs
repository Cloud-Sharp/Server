using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceRolePermissionConfiguration : IEntityTypeConfiguration<SpaceRolePermissionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceRolePermissionEntity> builder)
    {
        builder.ToTable("space_role_permissions");

        builder.HasKey(entity => new { entity.RoleId, entity.PermissionId }).HasName("pk_space_role_permissions");
        builder.Property(entity => entity.RoleId).HasColumnName("role_id");
        builder.Property(entity => entity.PermissionId).HasColumnName("permission_id");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");

        builder.HasOne(entity => entity.Role)
            .WithMany()
            .HasForeignKey(entity => entity.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_space_role_permissions_role");
        builder.HasOne<PermissionEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.PermissionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_role_permissions_permission");
    }
}
