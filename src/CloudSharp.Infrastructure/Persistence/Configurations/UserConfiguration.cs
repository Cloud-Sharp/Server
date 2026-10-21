using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable(
            "users",
            table =>
            {
                table.HasCheckConstraint(
                    "chk_users_email_not_blank",
                    "btrim(email) <> '' AND btrim(normalized_email) <> ''");
                table.HasCheckConstraint(
                    "chk_users_user_name_pair",
                    "(user_name IS NULL AND normalized_user_name IS NULL) OR (user_name IS NOT NULL AND normalized_user_name IS NOT NULL)");
                table.HasCheckConstraint(
                    "chk_users_security_version_positive",
                    "security_version > 0");
                table.HasCheckConstraint(
                    "chk_users_version_positive",
                    "version > 0");
                table.HasCheckConstraint(
                    "chk_users_status_timestamps",
                    "(status <> 'SUSPENDED' OR suspended_at IS NOT NULL) AND (status <> 'DELETED' OR deleted_at IS NOT NULL)");
            });

        builder.HasKey(entity => entity.Id)
            .HasName("pk_users");

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(entity => entity.PublicId)
            .HasColumnName("public_id")
            .HasColumnType("uuid");

        builder.Property(entity => entity.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(entity => entity.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(entity => entity.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(30);

        builder.Property(entity => entity.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .HasMaxLength(30);

        builder.Property(entity => entity.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100);

        builder.Property(entity => entity.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(entity => entity.SystemRole)
            .HasColumnName("system_role")
            .HasColumnType("system_role_t")
            .HasDefaultValue(SystemRole.User)
            .ValueGeneratedNever();

        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .HasColumnType("user_status_t")
            .HasDefaultValue(UserStatus.Active)
            .ValueGeneratedNever();

        builder.Property(entity => entity.SecurityVersion)
            .HasColumnName("security_version")
            .HasDefaultValue(1L)
            .ValueGeneratedNever();

        builder.Property(entity => entity.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        builder.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz");

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.Property(entity => entity.SuspendedAt)
            .HasColumnName("suspended_at")
            .HasColumnType("timestamptz");

        builder.Property(entity => entity.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(entity => entity.PublicId)
            .HasDatabaseName("ux_users_public_id")
            .IsUnique();

        builder.HasIndex(entity => entity.NormalizedEmail)
            .HasDatabaseName("ux_users_normalized_email")
            .IsUnique();

        builder.HasIndex(entity => entity.NormalizedUserName)
            .HasDatabaseName("ux_users_normalized_user_name")
            .IsUnique()
            .HasFilter("normalized_user_name IS NOT NULL");

        builder.HasIndex(entity => new { entity.Status, entity.CreatedAt })
            .HasDatabaseName("idx_users_status_created_at")
            .IsDescending(false, true);

        builder.HasIndex(entity => new { entity.CreatedAt, entity.Id })
            .HasDatabaseName("idx_users_created_at_id")
            .IsDescending(true, true);
    }
}