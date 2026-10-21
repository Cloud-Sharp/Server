using System.Data;
using System.Data.Common;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Transactions;

[TestFixture]
public sealed class EfCoreTransactionExecutorAvailabilityTests
{
    private const string ConnectionString = "Host=localhost;Database=probe;Username=probe";

    [TestCase(false, "begin")]
    [TestCase(true, "begin")]
    [TestCase(false, "operation")]
    [TestCase(true, "operation")]
    [TestCase(false, "commit")]
    [TestCase(true, "commit")]
    [TestCase(false, "rollback")]
    [TestCase(true, "rollback")]
    [TestCase(false, "dispose")]
    [TestCase(true, "dispose")]
    public async Task ExecuteAsync_ShouldReturnDependencyUnavailable(bool generic, string boundary)
    {
        var interceptor = new TransactionInterceptor(boundary, AvailabilityFailure());
        await using var context = CreateContext(interceptor);
        context.Add(new PersistenceProbe());

        var result = await ExecuteAsync(context, generic, boundary, interceptor.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            Assert.That(result.Errors.Single().Message, Does.Not.Contain("private provider detail"));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            Assert.That(interceptor.BeginAttempts, Is.EqualTo(1));
        });
    }

    private static IEnumerable<TestCaseData> UnexpectedFailures()
    {
        foreach (var generic in new[] { false, true })
        foreach (var boundary in new[] { "begin", "operation", "commit", "rollback", "dispose" })
        {
            Exception[] failures =
            [
                new InvalidOperationException("programming failure"),
                new PostgresException("invalid SQL", "ERROR", "ERROR", "42601"),
                new OperationCanceledException(),
                new NpgsqlException("cancelled transport", new IOException("cancelled", new OperationCanceledException())),
            ];
            foreach (var exception in failures)
            {
                yield return new TestCaseData(generic, boundary, exception)
                    .SetName($"ExecuteAsync_ShouldPreserveUnexpectedFailure({generic},{boundary},{exception.GetType().Name})");
            }
        }
    }

    [TestCaseSource(nameof(UnexpectedFailures))]
    public void ExecuteAsync_ShouldPreserveUnexpectedFailure(bool generic, string boundary, Exception exception)
    {
        using var context = CreateContext(new TransactionInterceptor(boundary, exception));
        context.Add(new PersistenceProbe());

        var thrown = Assert.CatchAsync(async () => await ExecuteAsync(context, generic, boundary, exception));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(exception));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldReturnDependencyUnavailableWithDefaultProviderStrategy(bool generic)
    {
        var interceptor = new TransactionInterceptor("begin", AvailabilityFailure());
        await using var context = CreateContext(interceptor, ExecutionMode.Provider);
        context.Add(new PersistenceProbe());

        var result = await ExecuteAsync(context, generic);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            Assert.That(interceptor.BeginAttempts, Is.EqualTo(1));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldAllowExecutionStrategyToRetry(bool generic)
    {
        var interceptor = new TransactionInterceptor("begin", AvailabilityFailure(), failuresRemaining: 1);
        await using var context = CreateContext(interceptor, ExecutionMode.RetryOnce);
        context.Add(new PersistenceProbe());
        var trackedCounts = new List<int>();

        var result = await ExecuteAsync(context, generic, onOperation: () =>
            trackedCounts.Add(context.ChangeTracker.Entries().Count()));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(interceptor.BeginAttempts, Is.EqualTo(2));
            Assert.That(trackedCounts, Is.EqualTo(new[] { 0 }));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldReturnDependencyUnavailableAfterRetryLimit(bool generic)
    {
        var interceptor = new TransactionInterceptor("begin", AvailabilityFailure());
        await using var context = CreateContext(interceptor, ExecutionMode.RetryOnce);

        var result = await ExecuteAsync(context, generic);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            Assert.That(interceptor.BeginAttempts, Is.EqualTo(2));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldForwardCancellationToken(bool generic)
    {
        using var source = new CancellationTokenSource();
        var interceptor = new TransactionInterceptor();
        await using var context = CreateContext(interceptor);
        var operationToken = CancellationToken.None;

        var result = await ExecuteAsync(context, generic, onToken: token => operationToken = token,
            cancellationToken: source.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(interceptor.BeginToken, Is.EqualTo(source.Token));
            Assert.That(operationToken, Is.EqualTo(source.Token));
            Assert.That(interceptor.CommitToken, Is.EqualTo(source.Token));
        });
    }

    private static async Task<Result> ExecuteAsync(
        AppDbContext context,
        bool generic,
        string? boundary = null,
        Exception? exception = null,
        Action? onOperation = null,
        Action<CancellationToken>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        Result Operation(CancellationToken token)
        {
            onOperation?.Invoke();
            onToken?.Invoke(token);
            if (boundary == "operation")
            {
                throw exception!;
            }

            return boundary == "rollback" ? Result.Fail("expected business failure") : Result.Ok();
        }

        var executor = new EfCoreTransactionExecutor(context);
        if (generic)
        {
            var result = await executor.ExecuteAsync(
                token => Task.FromResult(Operation(token).ToResult(42)), cancellationToken: cancellationToken);
            return result.ToResult();
        }

        return await executor.ExecuteAsync(
            token => Task.FromResult(Operation(token)), cancellationToken: cancellationToken);
    }

    private static AppDbContext CreateContext(
        TransactionInterceptor interceptor,
        ExecutionMode executionMode = ExecutionMode.NonRetrying)
    {
        var options = PersistenceProbeModel.CreateOptions(ConnectionString)
            .AddInterceptors(new SuppressConnectionOpenInterceptor(), interceptor);
        if (executionMode != ExecutionMode.Provider)
        {
            options.UseNpgsql(ConnectionString, provider => provider.ExecutionStrategy(dependencies =>
                executionMode == ExecutionMode.RetryOnce
                    ? new RetryOnceStrategy(dependencies)
                    : new NonRetryingExecutionStrategy(dependencies)));
        }

        return new AppDbContext(options.Options);
    }

    private enum ExecutionMode
    {
        NonRetrying,
        RetryOnce,
        Provider,
    }

    private static NpgsqlException AvailabilityFailure() =>
        new("private provider detail", new IOException("private transport detail"));

    private sealed class RetryOnceStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            PostgresAvailabilityErrors.IsDependencyUnavailable(exception);
    }

    private sealed class SuppressConnectionOpenInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class TransactionInterceptor(
        string? boundary = null,
        Exception? exception = null,
        int failuresRemaining = int.MaxValue) : DbTransactionInterceptor
    {
        private int _failuresRemaining = failuresRemaining;

        public Exception Exception { get; } = exception ?? new InvalidOperationException("unused failure");
        public int BeginAttempts { get; private set; }
        public CancellationToken BeginToken { get; private set; }
        public CancellationToken CommitToken { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            BeginAttempts++;
            BeginToken = cancellationToken;
            ThrowAt("begin");
            return ValueTask.FromResult(InterceptionResult<DbTransaction>.SuppressWithResult(
                new ProbeTransaction(connection, eventData.IsolationLevel, this)));
        }

        private void ThrowAt(string stage)
        {
            if (boundary == stage && _failuresRemaining-- > 0)
            {
                throw Exception;
            }
        }

        private sealed class ProbeTransaction(
            DbConnection connection,
            IsolationLevel isolationLevel,
            TransactionInterceptor owner) : DbTransaction
        {
            private bool _disposed;

            public override IsolationLevel IsolationLevel => isolationLevel;
            protected override DbConnection DbConnection => connection;

            public override void Commit() => owner.ThrowAt("commit");
            public override void Rollback() => owner.ThrowAt("rollback");

            public override Task CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.CommitToken = cancellationToken;
                Commit();
                return Task.CompletedTask;
            }

            public override Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                Rollback();
                return Task.CompletedTask;
            }

            public override ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    owner.ThrowAt("dispose");
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
