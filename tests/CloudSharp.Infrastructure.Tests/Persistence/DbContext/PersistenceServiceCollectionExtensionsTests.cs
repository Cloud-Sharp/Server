using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.DbContext;

[TestFixture]
public sealed class PersistenceServiceCollectionExtensionsTests
{
    [Test]
    public void AddPostgreSqlPersistence_ShouldRegisterNpgsqlDbContext()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();

        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.That(dbContext.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
    }

    [Test]
    public void AddPostgreSqlPersistence_ShouldRegisterDbContextAsScoped()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Multiple(() =>
        {
            Assert.That(sameScope, Is.SameAs(first));
            Assert.That(otherScope, Is.Not.SameAs(first));
        });
    }

    [Test]
    public void AddPostgreSqlPersistence_ShouldRegisterTransactionServicesAsScoped()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstExecutor = firstScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var sameExecutor = firstScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var otherExecutor = secondScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var unitOfWork = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dapperSession = firstScope.ServiceProvider.GetRequiredService<DapperDbSession>();

        Assert.Multiple(() =>
        {
            Assert.That(sameExecutor, Is.SameAs(firstExecutor));
            Assert.That(otherExecutor, Is.Not.SameAs(firstExecutor));
            Assert.That(unitOfWork, Is.TypeOf<EfCoreUnitOfWork>());
            Assert.That(dapperSession, Is.Not.Null);
        });
    }

    [Test]
    public void AddPostgreSqlPersistence_ShouldRejectMissingConnectionString()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPostgreSqlPersistence(configuration));

        Assert.That(exception!.Message, Does.Contain("ConnectionStrings:PostgreSql"));
    }

    private static IConfiguration CreateConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString,
            })
            .Build();
}
