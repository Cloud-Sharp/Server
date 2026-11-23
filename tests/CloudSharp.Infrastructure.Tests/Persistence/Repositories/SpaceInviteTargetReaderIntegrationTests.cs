using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SpaceInviteTargetReaderIntegrationTests
{
    private const long SpaceId = 7;
    private const string UpdateSpaceSql = "UPDATE spaces SET owner_user_id = 42, status = 'ARCHIVED' WHERE id = 7";
    private static readonly Guid PublicId = Guid.CreateVersion7();

    private PostgreSqlContainer? postgres;
    private NpgsqlConnection? writer;
    private ServiceProvider? provider;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_invite_target_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        writer = new NpgsqlConnection(postgres.GetConnectionString());
        await writer.OpenAsync();
        await writer.ExecuteAsync("""
            CREATE TYPE space_status_t AS ENUM ('ACTIVE', 'ARCHIVED', 'DELETING', 'DELETED');
            CREATE TABLE spaces (
                id bigint PRIMARY KEY,
                public_id uuid NOT NULL,
                owner_user_id bigint NOT NULL,
                status space_status_t NOT NULL
            );
            INSERT INTO spaces VALUES (@SpaceId, @PublicId, 21, 'ACTIVE');
            """, new { SpaceId, PublicId });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString(),
            })
            .Build();
        provider = new ServiceCollection()
            .AddPostgreSqlPersistence(configuration)
            .BuildServiceProvider();
    }

    [SetUp]
    public async Task SetUp()
    {
        await writer!.ExecuteAsync(
            "UPDATE spaces SET owner_user_id = 21, status = 'ACTIVE' WHERE id = @SpaceId",
            new { SpaceId });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (provider is not null)
        {
            await provider.DisposeAsync();
        }

        if (writer is not null)
        {
            await writer.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task ReadLockedAsync_ShouldBlockSpaceUpdateUntilTransactionEnds()
    {
        await using var scope = provider!.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISpaceInviteTargetReader>();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        var initial = await executor.ExecuteAsync<SpaceInviteTargetSnapshot?>(async cancellationToken =>
        {
            var result = await reader.ReadLockedAsync(SpaceId, cancellationToken);

            await using var competingTransaction = await writer!.BeginTransactionAsync(cancellationToken);
            var shared = await writer.QuerySingleAsync<long>(
                "SELECT id FROM spaces WHERE id = @SpaceId FOR SHARE NOWAIT",
                new { SpaceId },
                competingTransaction);
            Assert.That(shared, Is.EqualTo(SpaceId));

            await writer.ExecuteAsync("SET LOCAL lock_timeout = '250ms'", transaction: competingTransaction);
            var blocked = Assert.ThrowsAsync<PostgresException>(async () =>
                await writer.ExecuteAsync(UpdateSpaceSql, transaction: competingTransaction));
            Assert.That(blocked!.SqlState, Is.EqualTo(PostgresErrorCodes.LockNotAvailable));
            await competingTransaction.RollbackAsync(cancellationToken);

            return result;
        });

        Assert.That(initial.IsSuccess, Is.True, initial.ToString());
        Assert.That(initial.Value, Is.EqualTo(new SpaceInviteTargetSnapshot(SpaceId, PublicId, 21, SpaceStatus.Active)));
        Assert.That(await writer!.ExecuteAsync(UpdateSpaceSql), Is.EqualTo(1));
        var updated = await executor.ExecuteAsync<SpaceInviteTargetSnapshot?>(
            cancellationToken => reader.ReadLockedAsync(SpaceId, cancellationToken));
        Assert.That(updated.IsSuccess, Is.True, updated.ToString());
        Assert.That(updated.Value, Is.EqualTo(new SpaceInviteTargetSnapshot(SpaceId, PublicId, 42, SpaceStatus.Archived)));
    }

    [Test]
    public async Task ReadLockedAsync_WithMissingSpace_ShouldReturnNull()
    {
        await using var scope = provider!.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISpaceInviteTargetReader>();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        var result = await executor.ExecuteAsync<SpaceInviteTargetSnapshot?>(
            cancellationToken => reader.ReadLockedAsync(SpaceId + 1, cancellationToken));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Null);
    }
}
