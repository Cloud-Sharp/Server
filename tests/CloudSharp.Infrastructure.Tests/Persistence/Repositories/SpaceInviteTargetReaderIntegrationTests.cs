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

    [Test]
    public async Task ReadLockedAsync_ShouldBlockSpaceUpdateUntilTransactionEnds()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_invite_target_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        var publicId = Guid.CreateVersion7();
        var connectionString = postgres.GetConnectionString();
        await using var writer = new NpgsqlConnection(connectionString);
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
            """, new { SpaceId, PublicId = publicId });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection().AddPostgreSqlPersistence(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISpaceInviteTargetReader>();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        var initial = await executor.ExecuteAsync<SpaceInviteTargetSnapshot?>(async cancellationToken =>
        {
            var result = await reader.ReadLockedAsync(SpaceId, cancellationToken);

            await using var competingTransaction = await writer.BeginTransactionAsync(cancellationToken);
            await writer.ExecuteAsync("SET LOCAL lock_timeout = '250ms'", transaction: competingTransaction);
            var blocked = Assert.ThrowsAsync<PostgresException>(async () =>
                await writer.ExecuteAsync(UpdateSpaceSql, transaction: competingTransaction));
            Assert.That(blocked!.SqlState, Is.EqualTo(PostgresErrorCodes.LockNotAvailable));
            await competingTransaction.RollbackAsync(cancellationToken);

            return result;
        });

        Assert.That(initial.Value, Is.EqualTo(new SpaceInviteTargetSnapshot(SpaceId, publicId, 21, SpaceStatus.Active)));
        Assert.That(await writer.ExecuteAsync(UpdateSpaceSql), Is.EqualTo(1));
        var updated = await executor.ExecuteAsync<SpaceInviteTargetSnapshot?>(
            cancellationToken => reader.ReadLockedAsync(SpaceId, cancellationToken));
        Assert.That(updated.Value, Is.EqualTo(new SpaceInviteTargetSnapshot(SpaceId, publicId, 42, SpaceStatus.Archived)));
    }
}
