using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// CloudSharp의 PostgreSQL 영속성 모델과 변경 추적 단위를 제공한다.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : Microsoft.EntityFrameworkCore.DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SpaceEntity> Spaces => Set<SpaceEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<SpaceRoleEntity> SpaceRoles => Set<SpaceRoleEntity>();
    public DbSet<SpaceRolePermissionEntity> SpaceRolePermissions => Set<SpaceRolePermissionEntity>();
    public DbSet<SpaceMembershipEntity> SpaceMemberships => Set<SpaceMembershipEntity>();
    public DbSet<SpaceMembershipRoleEntity> SpaceMembershipRoles => Set<SpaceMembershipRoleEntity>();
    public DbSet<SpaceInviteEntity> SpaceInvites => Set<SpaceInviteEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
