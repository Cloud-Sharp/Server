using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

public sealed class SpaceMembershipRoleConfiguration : IEntityTypeConfiguration<SpaceMembershipRoleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpaceMembershipRoleEntity> builder)
    {
        builder.ToTable("space_membership_roles");

        builder.HasKey(entity => new { entity.MembershipId, entity.RoleId }).HasName("pk_space_membership_roles");
        builder.Property(entity => entity.MembershipId).HasColumnName("membership_id");
        builder.Property(entity => entity.RoleId).HasColumnName("role_id");
        builder.Property(entity => entity.SpaceId).HasColumnName("space_id");
        builder.Property(entity => entity.AssignedByUserId).HasColumnName("assigned_by_user_id");
        builder.Property(entity => entity.AssignedAt).HasColumnName("assigned_at").HasColumnType("timestamptz");

        builder.HasOne(entity => entity.Membership)
            .WithMany()
            .HasForeignKey(entity => new { entity.MembershipId, entity.SpaceId })
            .HasPrincipalKey(entity => new { entity.Id, entity.SpaceId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_space_membership_roles_membership");
        builder.HasOne<SpaceRoleEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.RoleId, entity.SpaceId })
            .HasPrincipalKey(entity => new { entity.Id, entity.SpaceId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_membership_roles_role");
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_space_membership_roles_assigned_by_user");

        builder.HasIndex(entity => new { entity.RoleId, entity.MembershipId })
            .HasDatabaseName("idx_space_membership_roles_role_membership");
        builder.HasIndex(entity => new { entity.SpaceId, entity.MembershipId })
            .HasDatabaseName("idx_space_membership_roles_space_membership");
    }
}
