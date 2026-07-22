using System.Data;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Dapper;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Transactions;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class EfCoreTransactionExecutorTests
{
    private const string CreateProbeTableSql = """
        CREATE TEMP TABLE transaction_probe (
            id integer PRIMARY KEY,
            source text NOT NULL
        )
        """;

    private PostgreSqlContainer? postgres;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_transaction_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task ExecuteAsync_OnSuccessfulResult_ShouldCommitDapperChange()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var result = await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            return Result.Ok();
        });

        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsyncOfT_OnSuccessfulResult_ShouldCommitAndReturnValue()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var result = await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            return Result.Ok("committed");
        });

        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("committed"));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_OnFailedResult_ShouldRollbackDapperChange()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var result = await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            return Result.Fail("expected failure");
        });

        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(count, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_OnException_ShouldRollbackAndRethrow()
    {
        await using var database = await CreateDatabaseScopeAsync();

        async Task<Result> Operation(CancellationToken cancellationToken)
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            throw new InvalidOperationException("expected failure");
        }

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await database.TransactionExecutor.ExecuteAsync(Operation));
        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("expected failure"));
            Assert.That(count, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldShareTransactionWithDapperCommand()
    {
        await using var database = await CreateDatabaseScopeAsync();
        System.Data.Common.DbTransaction? dapperTransaction = null;
        System.Data.Common.DbTransaction? efTransaction = null;

        await database.TransactionExecutor.ExecuteAsync(cancellationToken =>
        {
            var command = database.DapperSession.CreateCommand(
                "SELECT 1",
                cancellationToken: cancellationToken);
            dapperTransaction = command.Transaction as System.Data.Common.DbTransaction;
            efTransaction = database.DbContext.Database.CurrentTransaction?.GetDbTransaction();
            return Task.FromResult(Result.Ok());
        });

        Assert.Multiple(() =>
        {
            Assert.That(dapperTransaction, Is.Not.Null);
            Assert.That(dapperTransaction, Is.SameAs(efTransaction));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithRepeatableRead_ShouldApplyIsolationLevel()
    {
        await using var database = await CreateDatabaseScopeAsync();
        string? actualIsolationLevel = null;

        var result = await database.TransactionExecutor.ExecuteAsync(
            async cancellationToken =>
            {
                var command = database.DapperSession.CreateCommand(
                    "SHOW transaction_isolation",
                    cancellationToken: cancellationToken);
                actualIsolationLevel = await database.DapperSession.Connection
                    .QuerySingleAsync<string>(command);
                return Result.Ok();
            },
            IsolationLevel.RepeatableRead);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(actualIsolationLevel, Is.EqualTo("repeatable read"));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithDapperAndEfCore_ShouldCommitBothChanges()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var result = await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            await database.DbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO transaction_probe (id, source) VALUES (2, 'ef-core')",
                cancellationToken);

            var saveResult = await database.UnitOfWork.SaveChangesAsync(cancellationToken);
            return saveResult.IsSuccess
                ? Result.Ok()
                : Result.Fail(saveResult.Errors);
        });

        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithDapperAndEfCoreFailure_ShouldRollbackBothChanges()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var result = await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
        {
            await InsertWithDapperAsync(database, 1, cancellationToken);
            await database.DbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO transaction_probe (id, source) VALUES (2, 'ef-core')",
                cancellationToken);

            var saveResult = await database.UnitOfWork.SaveChangesAsync(cancellationToken);
            return saveResult.IsSuccess
                ? Result.Fail("expected failure")
                : Result.Fail(saveResult.Errors);
        });

        var count = await CountRowsAsync(database);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(count, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_WhenNested_ShouldRejectInnerTransaction()
    {
        await using var database = await CreateDatabaseScopeAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await database.TransactionExecutor.ExecuteAsync(async cancellationToken =>
            {
                await database.TransactionExecutor.ExecuteAsync(
                    _ => Task.FromResult(Result.Ok()),
                    cancellationToken: cancellationToken);
                return Result.Ok();
            }));

        Assert.That(
            exception!.Message,
            Is.EqualTo("Nested database transactions are not supported."));
    }

    [Test]
    public async Task ExecuteAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        await using var database = await CreateDatabaseScopeAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var operationInvoked = false;

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await database.TransactionExecutor.ExecuteAsync(
                _ =>
                {
                    operationInvoked = true;
                    return Task.FromResult(Result.Ok());
                },
                cancellationToken: cts.Token));

        Assert.That(operationInvoked, Is.False);
    }

    private async Task<DatabaseScope> CreateDatabaseScopeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = postgres!.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        var database = new DatabaseScope(provider, scope);

        await database.DbContext.Database.OpenConnectionAsync();
        var createCommand = database.DapperSession.CreateCommand(CreateProbeTableSql);
        await database.DapperSession.Connection.ExecuteAsync(createCommand);

        return database;
    }

    private static Task InsertWithDapperAsync(
        DatabaseScope database,
        int id,
        CancellationToken cancellationToken)
    {
        var command = database.DapperSession.CreateCommand(
            "INSERT INTO transaction_probe (id, source) VALUES (@Id, 'dapper')",
            new { Id = id },
            cancellationToken: cancellationToken);
        return database.DapperSession.Connection.ExecuteAsync(command);
    }

    private static Task<int> CountRowsAsync(DatabaseScope database)
    {
        var command = database.DapperSession.CreateCommand(
            "SELECT COUNT(*) FROM transaction_probe");
        return database.DapperSession.Connection.QuerySingleAsync<int>(command);
    }

    private sealed class DatabaseScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        public DatabaseScope(ServiceProvider provider, AsyncServiceScope scope)
        {
            this.provider = provider;
            this.scope = scope;
            DbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            TransactionExecutor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            UnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            DapperSession = scope.ServiceProvider.GetRequiredService<DapperDbSession>();
        }

        public AppDbContext DbContext { get; }

        public ITransactionExecutor TransactionExecutor { get; }

        public IUnitOfWork UnitOfWork { get; }

        public DapperDbSession DapperSession { get; }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
