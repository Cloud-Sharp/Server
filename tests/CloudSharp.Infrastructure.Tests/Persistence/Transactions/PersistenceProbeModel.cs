using CloudSharp.Infrastructure.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CloudSharp.Infrastructure.Tests.Persistence.Transactions;

/// <summary>
/// Production entity 없이 UoW와 transaction adapter를 검증하는 테스트 전용 모델.
/// </summary>
internal static class PersistenceProbeModel
{
    internal static DbContextOptionsBuilder<AppDbContext> CreateOptions(string connectionString)
    {
        using var modelContext = new ProbeModelContext(
            new DbContextOptionsBuilder<ProbeModelContext>().UseNpgsql(connectionString).Options);

        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseModel(modelContext.GetService<IDesignTimeModel>().Model);
    }

    private sealed class ProbeModelContext(DbContextOptions<ProbeModelContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<PersistenceProbe>();
            entity.ToTable("persistence_probe", table =>
                table.HasCheckConstraint("chk_probe_version_positive", "version > 0"));
            entity.HasKey(probe => probe.Id);
            entity.Property(probe => probe.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(probe => probe.Email).HasColumnName("email").IsRequired();
            entity.Property(probe => probe.UserName).HasColumnName("user_name");
            entity.Property(probe => probe.Source).HasColumnName("source").IsRequired();
            entity.Property(probe => probe.Version).HasColumnName("version").IsConcurrencyToken();
            entity.HasIndex(probe => probe.Email).HasDatabaseName("ux_users_normalized_email").IsUnique();
            entity.HasIndex(probe => probe.UserName).HasDatabaseName("ux_users_normalized_user_name").IsUnique();
            entity.HasIndex(probe => probe.Source).HasDatabaseName("ux_probe_source").IsUnique();
        }
    }
}

internal sealed class PersistenceProbe
{
    public long Id { get; set; }
    public string Email { get; set; } = "probe@example.test";
    public string? UserName { get; set; }
    public string Source { get; set; } = Guid.NewGuid().ToString();
    public long Version { get; set; } = 1;
}
