using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// PostgreSQL 영속성 구성 요소를 DI 컨테이너에 등록한다.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    // Stateless translator shared as a singleton so equal connection strings produce
    // equal DbContextOptions and EF reuses its cached internal service provider.
    private static readonly PostgresEnumNameTranslator EnumNameTranslator = new();

    /// <summary>
    /// <c>ConnectionStrings:PostgreSql</c> 연결 문자열을 사용해
    /// <see cref="AppDbContext"/>를 요청 범위로 등록한다.
    /// </summary>
    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is required.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                    .MapEnum<SystemRole>(SystemRoleEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<UserStatus>(UserStatusEnumName, nameTranslator: EnumNameTranslator)));
        services.AddScoped<ITransactionExecutor, EfCoreTransactionExecutor>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<DapperDbSession>();

        return services;
    }

    internal const string ConnectionStringName = "PostgreSql";

    internal const string SystemRoleEnumName = "system_role_t";
    internal const string UserStatusEnumName = "user_status_t";
}
