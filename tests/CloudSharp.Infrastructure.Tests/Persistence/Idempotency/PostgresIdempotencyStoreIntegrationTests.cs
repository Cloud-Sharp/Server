using System.Text.Json;
using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Idempotency;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Dapper;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Idempotency;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class PostgresIdempotencyStoreIntegrationTests
{
    private const string OperationId = "create-report";
    private const int ResultVersion = 1;
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;
    private string connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_idempotency_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();
        connectionString = postgres.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString,
            })
            .Build();
        services = new ServiceCollection()
            .AddPostgreSqlPersistence(configuration)
            .AddIdempotencyReplayType<ReportRecovery>(OperationId, ResultVersion)
            .BuildServiceProvider();

        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("CREATE TABLE idempotency_business_probe (id uuid PRIMARY KEY)");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task ReserveOrGetAsync_ShouldCommitOneReservationPerScope()
    {
        var first = CreateReservation();
        var second = first with { AttemptId = Guid.NewGuid() };
        var independent = second with
        {
            Request = second.Request with { Scope = second.Request.Scope with { Actor = "other-actor" } },
            AttemptId = Guid.NewGuid(),
        };

        var concurrent = await Task.WhenAll(ReserveAsync(first, Timestamp), ReserveAsync(second, Timestamp))
            .WaitAsync(TestTimeout);
        var otherScope = await ReserveAsync(independent, Timestamp);
        await using var connection = new NpgsqlConnection(connectionString);
        var persisted = (await connection.QueryAsync<Guid>(
            "SELECT attempt_id FROM idempotency_requests WHERE idempotency_key = @Key",
            new { first.Request.Key })).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(concurrent[0], Is.TypeOf<IdempotencyRecord.InProgress>());
            Assert.That(concurrent[1].Reservation, Is.EqualTo(concurrent[0].Reservation));
            Assert.That(new[] { first, second }, Does.Contain(concurrent[0].Reservation));
            Assert.That(otherScope.Reservation, Is.EqualTo(independent));
            Assert.That(persisted, Is.EquivalentTo(new[] { concurrent[0].Reservation.AttemptId, independent.AttemptId }));
        });
    }

    [Test]
    public async Task ReserveOrGetAsync_ShouldPreserveLockedReservationThenReclaimAnExpiredLease()
    {
        var first = CreateReservation();
        var original = (IdempotencyRecord.InProgress)await ReserveAsync(first, Timestamp);
        var next = first with { AttemptId = Guid.NewGuid() };
        var expiredAt = original.LeaseExpiresAt.AddSeconds(1);
        var lockAcquired = new TaskCompletionSource<Result<IdempotencyRecord?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var workerScope = CreateScope();
        var worker = workerScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var workerTransaction = workerScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var holdingLock = workerTransaction.ExecuteAsync(async cancellationToken =>
        {
            var locked = await worker.LockAsync(first.Request.Scope, first.Request.Key, cancellationToken);
            lockAcquired.SetResult(locked);
            await releaseLock.Task.WaitAsync(cancellationToken);
            return Result.Fail("Rollback the test transaction holding the lock.");
        });

        try
        {
            var locked = RequireValue(await lockAcquired.Task.WaitAsync(TestTimeout));
            Assert.That(locked, Is.EqualTo(original));
            using var contenderTimeout = new CancellationTokenSource(TestTimeout);
            var whileLocked = await ReserveAsync(next, expiredAt, contenderTimeout.Token);
            Assert.That(whileLocked, Is.EqualTo(original));
        }
        finally
        {
            releaseLock.TrySetResult();
            await holdingLock.WaitAsync(TestTimeout);
        }

        var differentFingerprint = next with { Request = next.Request with { Fingerprint = "different" } };
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var mismatched = await store.ReserveOrGetAsync(differentFingerprint, expiredAt);
        var reclaimed = await ReserveAsync(next, expiredAt);
        var staleRelease = await store.ReleaseAsync(first);
        var staleCompletion = await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(
            cancellationToken => store.CompleteAsync(
                new IdempotencyRecord.Completed(first, ResultVersion, new IdempotencyRecovery.SecretNotReplayable(null), expiredAt),
                cancellationToken));

        var stillCurrent = await ReserveAsync(first, expiredAt);
        Assert.Multiple(() =>
        {
            Assert.That(mismatched.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Idempotency.KeyReused));
            Assert.That(reclaimed.Reservation, Is.EqualTo(next));
            Assert.That(staleRelease.IsSuccess, Is.True, staleRelease.ToString());
            Assert.That(staleRelease.Value, Is.False);
            Assert.That(staleCompletion.IsFailed, Is.True);
            Assert.That(stillCurrent, Is.EqualTo(reclaimed));
        });
    }

    [Test]
    public async Task ReserveOrGetAsync_ShouldRejectDifferentInputBeforeDecodingTheStoredVersion()
    {
        var reservation = CreateReservation();
        var recovery = new IdempotencyRecovery.Replayable<ReportRecovery>(new ReportRecovery(Guid.NewGuid(), 1));
        await ReserveAsync(reservation, Timestamp);
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var completed = await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(
            ct => store.CompleteAsync(new IdempotencyRecord.Completed(reservation, ResultVersion, recovery, Timestamp), ct));
        Assert.That(completed.IsSuccess, Is.True, completed.ToString());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(
            "UPDATE idempotency_requests SET result_version = 2 WHERE idempotency_key = @Key",
            new { reservation.Request.Key });
        var differentInput = reservation with
        {
            Request = reservation.Request with { Fingerprint = "different" },
            AttemptId = Guid.NewGuid(),
        };

        var result = await store.ReserveOrGetAsync(differentInput, Timestamp.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(ErrorCodes.Idempotency.KeyReused));
    }

    [Test]
    public async Task CompleteAsync_ShouldRollbackAndCommitWithTheBusinessMutation()
    {
        var reservation = CreateReservation();
        var recovery = new ReportRecovery(Guid.NewGuid(), Revision: 2);
        var completion = new IdempotencyRecord.Completed(
            reservation, ResultVersion, new IdempotencyRecovery.Replayable<ReportRecovery>(recovery), Timestamp);
        await ReserveAsync(reservation, Timestamp);

        var rolledBack = await CompleteWithBusinessChangeAsync(completion, recovery.ResourceId, shouldCommit: false);
        await using var connection = new NpgsqlConnection(connectionString);
        var afterRollback = await ReadRowAsync(connection, reservation);
        var rolledBackBusinessCount = await CountBusinessRowsAsync(connection, recovery.ResourceId);

        var committed = await CompleteWithBusinessChangeAsync(completion, recovery.ResourceId, shouldCommit: true);
        var afterCommit = await ReadRowAsync(connection, reservation);
        var committedBusinessCount = await CountBusinessRowsAsync(connection, recovery.ResourceId);
        var replay = await ReserveAsync(reservation with { AttemptId = Guid.NewGuid() }, Timestamp.AddMinutes(1));
        await using var verificationScope = CreateScope();
        var found = RequireValue(await verificationScope.ServiceProvider.GetRequiredService<IIdempotencyStore>()
            .FindCompletionAsync(reservation));

        Assert.Multiple(() =>
        {
            Assert.That(rolledBack.IsFailed, Is.True);
            Assert.That(afterRollback.State, Is.EqualTo("IN_PROGRESS"));
            Assert.That(afterRollback.RecoveryPayload, Is.Null);
            Assert.That(rolledBackBusinessCount, Is.Zero);
            Assert.That(committed.IsSuccess, Is.True, committed.ToString());
            Assert.That(afterCommit.State, Is.EqualTo("COMPLETED"));
            Assert.That(afterCommit.RecoveryKind, Is.EqualTo("REPLAYABLE"));
            Assert.That(afterCommit.RecoveryPayload, Does.Contain(recovery.ResourceId.ToString()));
            Assert.That(committedBusinessCount, Is.EqualTo(1));
            Assert.That(replay, Is.EqualTo(completion));
            Assert.That(found, Is.EqualTo(completion));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompleteAsync_ShouldRetainOnlyPublicIdAndAllowKeyReuseAfterExpiry(bool hasPublicId)
    {
        var reservation = CreateReservation();
        var resourceId = hasPublicId ? Guid.NewGuid() : (Guid?)null;
        var completion = new IdempotencyRecord.Completed(
            reservation, ResultVersion, new IdempotencyRecovery.SecretNotReplayable(resourceId), Timestamp);
        await ReserveAsync(reservation, Timestamp);

        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var written = await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(
            cancellationToken => store.CompleteAsync(completion, cancellationToken));
        Assert.That(written.IsSuccess, Is.True, written.ToString());

        await using var connection = new NpgsqlConnection(connectionString);
        var persisted = await ReadRowAsync(connection, reservation);
        var justBeforeExpiry = await ReserveAsync(
            reservation with { AttemptId = Guid.NewGuid() }, completion.ExpiresAt.AddSeconds(-1));
        var next = reservation with
        {
            Request = reservation.Request with { Fingerprint = "a-new-fingerprint" },
            AttemptId = Guid.NewGuid(),
        };
        var recycled = await ReserveAsync(next, completion.ExpiresAt);
        var afterReuse = await ReadRowAsync(connection, reservation);
        var formerCompletion = RequireValue(await store.FindCompletionAsync(reservation));

        Assert.Multiple(() =>
        {
            Assert.That(persisted.RecoveryKind, Is.EqualTo("SECRET_NOT_REPLAYABLE"));
            Assert.That(persisted.RecoveryPayload, Is.EqualTo(JsonSerializer.Serialize(resourceId)));
            Assert.That(persisted.CompletedAt, Is.EqualTo(Timestamp.UtcDateTime));
            Assert.That(persisted.ExpiresAt, Is.EqualTo(completion.ExpiresAt.UtcDateTime));
            Assert.That(justBeforeExpiry, Is.EqualTo(completion));
            Assert.That(recycled, Is.TypeOf<IdempotencyRecord.InProgress>());
            Assert.That(recycled.Reservation, Is.EqualTo(next));
            Assert.That(afterReuse.RecoveryPayload, Is.Null);
            Assert.That(afterReuse.CompletedAt, Is.Null);
            Assert.That(afterReuse.ExpiresAt, Is.Null);
            Assert.That(formerCompletion, Is.Null);
        });
    }

    private AsyncServiceScope CreateScope() => services!.CreateAsyncScope();

    private async Task<IdempotencyRecord> ReserveAsync(
        IdempotencyReservation candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        return RequireValue(await store.ReserveOrGetAsync(candidate, now, cancellationToken));
    }

    private async Task<Result> CompleteWithBusinessChangeAsync(
        IdempotencyRecord.Completed completion,
        Guid resourceId,
        bool shouldCommit)
    {
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var session = scope.ServiceProvider.GetRequiredService<DapperDbSession>();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        return await executor.ExecuteAsync(async cancellationToken =>
        {
            var request = completion.Reservation.Request;
            var locked = RequireValue(await store.LockAsync(request.Scope, request.Key, cancellationToken));
            Assert.That(locked?.Reservation, Is.EqualTo(completion.Reservation));
            await session.Connection.ExecuteAsync(session.CreateCommand(
                "INSERT INTO idempotency_business_probe (id) VALUES (@Id)",
                new { Id = resourceId },
                cancellationToken: cancellationToken));
            var result = await store.CompleteAsync(completion, cancellationToken);
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            return shouldCommit ? result : Result.Fail("Rollback the business change and completion record.");
        });
    }

    private static Task<int> CountBusinessRowsAsync(NpgsqlConnection connection, Guid resourceId) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM idempotency_business_probe WHERE id = @Id",
            new { Id = resourceId });

    private static Task<StoredRow> ReadRowAsync(NpgsqlConnection connection, IdempotencyReservation reservation) =>
        connection.QuerySingleAsync<StoredRow>(
            """
            SELECT state AS State, recovery_kind AS RecoveryKind, recovery_payload::text AS RecoveryPayload,
                   completed_at AS CompletedAt, expires_at AS ExpiresAt
            FROM idempotency_requests
            WHERE idempotency_key = @Key
            """,
            new { reservation.Request.Key });

    private static IdempotencyReservation CreateReservation() => new(
        new IdempotencyRequestIdentity(
            new IdempotencyScope("actor", "POST", "/reports"), Guid.NewGuid().ToString("N"), "fingerprint", OperationId),
        Guid.NewGuid());

    private static T RequireValue<T>(Result<T> result)
    {
        Assert.That(result.IsSuccess, Is.True, result.ToString());
        return result.Value;
    }

    public sealed record ReportRecovery(Guid ResourceId, int Revision);

    private sealed class StoredRow
    {
        public string State { get; init; } = null!;
        public string? RecoveryKind { get; init; }
        public string? RecoveryPayload { get; init; }
        public DateTime? CompletedAt { get; init; }
        public DateTime? ExpiresAt { get; init; }
    }
}
