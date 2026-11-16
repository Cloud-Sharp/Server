using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<PermissionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionEntity> builder)
    {
        builder.ToTable(
            "permissions",
            table => table.HasCheckConstraint("chk_permissions_code_not_blank", "btrim(code) <> ''"));

        builder.HasKey(entity => entity.Id).HasName("pk_permissions");
        builder.Property(entity => entity.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(entity => entity.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");

        builder.HasIndex(entity => entity.Code)
            .HasDatabaseName("ux_permissions_code")
            .IsUnique();
    }
}
