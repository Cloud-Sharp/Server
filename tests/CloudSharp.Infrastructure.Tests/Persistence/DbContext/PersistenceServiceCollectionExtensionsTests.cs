using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Repositories;
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
    public void AddPostgreSqlPersistence_ShouldRegisterUserRepositoryAsScoped()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IUserRepository>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IUserRepository>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.Multiple(() =>
        {
            Assert.That(sameScope, Is.SameAs(first));
            Assert.That(otherScope, Is.Not.SameAs(first));
            Assert.That(first, Is.TypeOf<UserRepository>());
        });
    }

    [Test]
    public void AddPostgreSqlPersistence_ShouldRegisterSpaceRepositoryAsScoped()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ISpaceRepository>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ISpaceRepository>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<ISpaceRepository>();

        Assert.Multiple(() =>
        {
            Assert.That(sameScope, Is.SameAs(first));
            Assert.That(otherScope, Is.Not.SameAs(first));
            Assert.That(first, Is.TypeOf<SpaceRepository>());
        });
    }

    [Test]
    public void AddPostgreSqlPersistence_ShouldRegisterSpaceReadersAndBootstrapWriterAsScoped()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddSingleton<ITokenHasher>(new HmacSha256TokenHasher(new TokenHashingOptions
        {
            ActiveKey = Convert.ToBase64String(new byte[32]),
        }));
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstDetails = firstScope.ServiceProvider.GetRequiredService<ISpaceDetailsReader>();
        var firstList = firstScope.ServiceProvider.GetRequiredService<ISpaceListCandidateReader>();
        var firstAuthorization = firstScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationReader>();
        var firstRole = firstScope.ServiceProvider.GetRequiredService<ISpaceRoleReader>();
        var firstBootstrap = firstScope.ServiceProvider.GetRequiredService<ISpaceBootstrapWriter>();
        var sameDetails = firstScope.ServiceProvider.GetRequiredService<ISpaceDetailsReader>();
        var sameList = firstScope.ServiceProvider.GetRequiredService<ISpaceListCandidateReader>();
        var sameAuthorization = firstScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationReader>();
        var sameRole = firstScope.ServiceProvider.GetRequiredService<ISpaceRoleReader>();
        var sameBootstrap = firstScope.ServiceProvider.GetRequiredService<ISpaceBootstrapWriter>();
        var otherDetails = secondScope.ServiceProvider.GetRequiredService<ISpaceDetailsReader>();
        var otherList = secondScope.ServiceProvider.GetRequiredService<ISpaceListCandidateReader>();
        var otherAuthorization = secondScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationReader>();
        var otherRole = secondScope.ServiceProvider.GetRequiredService<ISpaceRoleReader>();
        var otherBootstrap = secondScope.ServiceProvider.GetRequiredService<ISpaceBootstrapWriter>();

        Assert.Multiple(() =>
        {
            Assert.That(sameDetails, Is.SameAs(firstDetails));
            Assert.That(sameList, Is.SameAs(firstList));
            Assert.That(sameAuthorization, Is.SameAs(firstAuthorization));
            Assert.That(sameRole, Is.SameAs(firstRole));
            Assert.That(sameBootstrap, Is.SameAs(firstBootstrap));
            Assert.That(otherDetails, Is.Not.SameAs(firstDetails));
            Assert.That(otherList, Is.Not.SameAs(firstList));
            Assert.That(otherAuthorization, Is.Not.SameAs(firstAuthorization));
            Assert.That(otherRole, Is.Not.SameAs(firstRole));
            Assert.That(otherBootstrap, Is.Not.SameAs(firstBootstrap));
            Assert.That(firstDetails, Is.TypeOf<SpaceDetailsReader>());
            Assert.That(firstList, Is.TypeOf<SpaceListCandidateReader>());
            Assert.That(firstAuthorization, Is.TypeOf<SpaceAuthorizationReader>());
            Assert.That(firstRole, Is.TypeOf<SpaceRoleReader>());
            Assert.That(firstBootstrap, Is.TypeOf<SpaceBootstrapWriter>());
        });
    }

    [TestCase(typeof(ISpaceMembershipReader), typeof(SpaceMembershipReader))]
    [TestCase(typeof(ISpaceMembershipRepository), typeof(SpaceMembershipRepository))]
    [TestCase(typeof(ISpaceOwnershipMembershipRepository), typeof(SpaceOwnershipMembershipRepository))]
    public void AddPostgreSqlPersistence_ShouldRegisterSpaceMembershipPortAsScoped(
        Type portType,
        Type implementationType)
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=cloudsharp;Username=cloudsharp");
        var services = new ServiceCollection();
        services.AddSingleton<ITokenHasher>(new HmacSha256TokenHasher(new TokenHashingOptions
        {
            ActiveKey = Convert.ToBase64String(new byte[32]),
        }));
        services.AddPostgreSqlPersistence(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService(portType);
        var sameScope = firstScope.ServiceProvider.GetRequiredService(portType);
        var otherScope = secondScope.ServiceProvider.GetRequiredService(portType);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf(implementationType));
            Assert.That(sameScope, Is.SameAs(first));
            Assert.That(otherScope, Is.Not.SameAs(first));
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
