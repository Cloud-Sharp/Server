using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Transactions;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class EfCoreUnitOfWorkIntegrationTests
{
    private PostgreSqlContainer? postgres;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_unit_of_work_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE persistence_probe RESTART IDENTITY");
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
    public async Task SaveChangesAsync_ShouldAssignIdentityAndPermitReadBeforeCommit()
    {
        await using var context = CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var executor = new EfCoreTransactionExecutor(context);

        var result = await executor.ExecuteAsync(async cancellationToken =>
        {
            var probe = new PersistenceProbe();
            context.Add(probe);
            Assert.That(probe.Id, Is.Zero);

            var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
            Assert.Multiple(() =>
            {
                Assert.That(saved.IsSuccess, Is.True);
                Assert.That(saved.Value, Is.EqualTo(1));
                Assert.That(probe.Id, Is.Positive);
                Assert.That(context.Database.CurrentTransaction, Is.Not.Null);
            });

            var reloaded = await context.Set<PersistenceProbe>().AsNoTracking()
                .SingleAsync(cancellationToken);
            return Result.Ok(reloaded.Id);
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.Positive);
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    [TestCase(true, ErrorCodes.User.EmailConflict)]
    [TestCase(false, ErrorCodes.User.NameConflict)]
    public async Task SaveChangesAsync_OnRealUserUniqueViolation_ShouldRollbackAndReturnStableError(
        bool duplicateEmail, string errorCode)
    {
        await using var context = CreateContext();
        var executor = new EfCoreTransactionExecutor(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        await SeedAsync(context, new PersistenceProbe { UserName = "original" });

        var result = await executor.ExecuteAsync(async cancellationToken =>
        {
            context.Add(new PersistenceProbe
            {
                Email = duplicateEmail ? "probe@example.test" : "second@example.test",
                UserName = duplicateEmail ? "second" : "original",
            });
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
            return saved.IsSuccess ? Result.Ok() : Result.Fail(saved.Errors);
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    [TestCase(true, "ux_probe_source")]
    [TestCase(false, "chk_probe_version_positive")]
    public async Task SaveChangesAsync_OnRealUnknownIntegrityViolation_ShouldPreserveInternalError(
        bool duplicateSource, string constraintName)
    {
        await using var context = CreateContext();
        var original = new PersistenceProbe();
        await SeedAsync(context, original);

        var result = await new EfCoreTransactionExecutor(context).ExecuteAsync(async cancellationToken =>
        {
            context.Add(new PersistenceProbe
            {
                Email = "second@example.test",
                Source = duplicateSource ? original.Source : Guid.NewGuid().ToString(),
                Version = duplicateSource ? 1 : 0,
            });
            var saved = await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            return saved.IsSuccess ? Result.Ok() : Result.Fail(saved.Errors);
        });

        var error = result.Errors.Single() as DbConstraintViolationError;
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.ConstraintName, Is.EqualTo(constraintName));
            Assert.That(error.Metadata, Is.Empty);
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SaveChangesAsync_OnConcurrentVersionChange_ShouldRejectStaleUpdate()
    {
        await using var context = CreateContext();
        var original = new PersistenceProbe();
        await SeedAsync(context, original);

        var result = await new EfCoreTransactionExecutor(context).ExecuteAsync(async cancellationToken =>
        {
            var snapshot = await context.Set<PersistenceProbe>().AsNoTracking()
                .SingleAsync(cancellationToken);
            var expectedVersion = snapshot.Version;

            await using var competingContext = CreateContext();
            await competingContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE persistence_probe SET version = version + 1, source = 'concurrent' WHERE id = {snapshot.Id}",
                cancellationToken);

            snapshot.Version++;
            snapshot.Source = "stale";
            context.Update(snapshot);
            context.Entry(snapshot).Property(probe => probe.Version).OriginalValue = expectedVersion;

            var saved = await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            return saved.IsSuccess ? Result.Ok() : Result.Fail(saved.Errors);
        });

        var persisted = await context.Set<PersistenceProbe>().AsNoTracking().SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
            Assert.That(persisted.Version, Is.EqualTo(2));
            Assert.That(persisted.Source, Is.EqualTo("concurrent"));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_AfterCommit_ShouldClearTracking(bool generic)
    {
        await using var context = CreateContext();

        var result = await ExecuteAsync(context, generic, async cancellationToken =>
        {
            context.Add(new PersistenceProbe());
            var saved = await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            return saved.IsSuccess ? Result.Ok() : Result.Fail(saved.Errors);
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_AfterRollback_ShouldDiscardSavedAndPendingTracking(bool generic)
    {
        await using var context = CreateContext();

        var result = await ExecuteAsync(context, generic, async cancellationToken =>
        {
            context.Add(new PersistenceProbe());
            await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            context.Add(new PersistenceProbe { Email = "pending@example.test" });
            return Result.Fail("expected failure");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ExecuteAsync_OnExceptionOrCancellation_ShouldRollbackAndClearTracking(
        bool generic, bool cancelled)
    {
        await using var context = CreateContext();
        Exception expectedException = cancelled ? new OperationCanceledException() : new InvalidOperationException();

        var thrown = Assert.CatchAsync(async () => await ExecuteAsync(context, generic, async cancellationToken =>
        {
            context.Add(new PersistenceProbe());
            await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            throw expectedException;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(expectedException));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_WhenNestedRejected_ShouldPreserveOuterTracking(bool nestedGeneric)
    {
        await using var context = CreateContext();

        var result = await new EfCoreTransactionExecutor(context).ExecuteAsync(async cancellationToken =>
        {
            var probe = new PersistenceProbe();
            context.Add(probe);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ExecuteAsync(context, nestedGeneric, _ => Task.FromResult(Result.Ok())));
            Assert.Multiple(() =>
            {
                Assert.That(context.ChangeTracker.Entries().Count(), Is.EqualTo(1));
                Assert.That(context.Entry(probe).State, Is.EqualTo(EntityState.Added));
                Assert.That(context.Database.CurrentTransaction, Is.Not.Null);
            });

            var saved = await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            return saved.IsSuccess ? Result.Ok() : Result.Fail(saved.Errors);
        });

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_OnRetry_ShouldBeginWithFreshTracking(bool generic)
    {
        await using var context = CreateContext(retry: true);
        var attempts = 0;

        var result = await ExecuteAsync(context, generic, async cancellationToken =>
        {
            attempts++;
            Assert.Multiple(() =>
            {
                Assert.That(context.ChangeTracker.Entries(), Is.Empty);
                Assert.That(context.Database.CurrentTransaction, Is.Not.Null);
            });
            context.Add(new PersistenceProbe());
            var saved = await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
            Assert.That(saved.IsSuccess, Is.True);

            if (attempts == 1)
            {
                throw new NpgsqlException("simulated transient transport failure", new IOException());
            }

            return Result.Ok();
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(context.Database.CurrentTransaction, Is.Null);
        });
        Assert.That(await context.Set<PersistenceProbe>().CountAsync(), Is.EqualTo(1));
    }

    private AppDbContext CreateContext(bool retry = false)
    {
        var options = PersistenceProbeModel.CreateOptions(postgres!.GetConnectionString());
        if (retry)
        {
            options.UseNpgsql(postgres.GetConnectionString(), npgsql =>
                npgsql.EnableRetryOnFailure(1, TimeSpan.Zero, null));
        }

        return new AppDbContext(options.Options);
    }

    private static Task<Result> ExecuteAsync(
        AppDbContext context,
        bool generic,
        Func<CancellationToken, Task<Result>> operation)
    {
        var executor = new EfCoreTransactionExecutor(context);
        return generic ? ExecuteGenericAsync() : executor.ExecuteAsync(operation);

        async Task<Result> ExecuteGenericAsync()
        {
            var result = await executor.ExecuteAsync(async cancellationToken =>
            {
                var operationResult = await operation(cancellationToken);
                return operationResult.IsSuccess ? Result.Ok(true) : Result.Fail<bool>(operationResult.Errors);
            });
            return result.IsSuccess ? Result.Ok() : Result.Fail(result.Errors);
        }
    }

    private static Task<Result<int>> SeedAsync(AppDbContext context, PersistenceProbe probe) =>
        new EfCoreTransactionExecutor(context).ExecuteAsync(async cancellationToken =>
        {
            context.Add(probe);
            return await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationToken);
        });
}
