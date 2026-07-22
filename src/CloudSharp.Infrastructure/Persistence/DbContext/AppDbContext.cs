using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// CloudSharp의 PostgreSQL 영속성 모델과 변경 추적 단위를 제공한다.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : Microsoft.EntityFrameworkCore.DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
