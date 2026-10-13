using System.Net.Sockets;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Transactions;

[TestFixture]
public sealed class EfCoreUnitOfWorkTests
{
    [TestCase("ux_users_normalized_email", ErrorCodes.User.EmailConflict)]
    [TestCase("ux_users_normalized_user_name", ErrorCodes.User.NameConflict)]
    public async Task SaveChangesAsync_OnKnownUserUniqueConstraint_ShouldReturnDomainError(
        string constraintName, string errorCode)
    {
        var exception = new DbUpdateException("save failed", CreatePostgresException("23505", constraintName));
        await using var context = CreateContext(new ThrowingSaveInterceptor(exception));

        var result = await new EfCoreUnitOfWork(context).SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(result.Errors.Single().Message, Does.Not.Contain(constraintName));
        });
    }

    [TestCase("23505", "ux_probe_source")]
    [TestCase("23505", "ux_users_public_id")]
    [TestCase("23505", null)]
    [TestCase("23514", "ux_users_normalized_email")]
    public async Task SaveChangesAsync_OnUnrecognizedIntegrityConstraint_ShouldPreserveInternalError(
        string sqlState, string? constraintName)
    {
        var exception = new DbUpdateException("save failed", CreatePostgresException(sqlState, constraintName));
        await using var context = CreateContext(new ThrowingSaveInterceptor(exception));

        var result = await new EfCoreUnitOfWork(context).SaveChangesAsync();

        var error = result.Errors.Single() as DbConstraintViolationError;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.OriginalException, Is.SameAs(exception));
            Assert.That(error.ConstraintName, Is.EqualTo(constraintName));
            Assert.That(error.Metadata, Is.Empty);
        });
    }

    [Test]
    public async Task SaveChangesAsync_OnConcurrencyFailure_ShouldReturnPreconditionFailed()
    {
        await using var context = CreateContext(new ThrowingSaveInterceptor(new DbUpdateConcurrencyException()));

        var result = await new EfCoreUnitOfWork(context).SaveChangesAsync();

        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
    }

    private static IEnumerable<TestCaseData> AvailabilityFailures()
    {
        foreach (var sqlState in new[] { "08006", "08003", "53100", "53300", "57P01", "57P02", "57P03", "57P04" })
        {
            yield return new TestCaseData(new DbUpdateException("save failed", CreatePostgresException(sqlState)))
                .SetName($"SaveChangesAsync_OnPostgreSqlAvailability_{sqlState}_ShouldReturnDependencyUnavailable");
        }

        yield return new TestCaseData(new NpgsqlException("transport failed", new IOException("transport failed")));
        yield return new TestCaseData(new NpgsqlException("transport failed", new SocketException()));
        yield return new TestCaseData(new NpgsqlException("command timed out", new TimeoutException()));
        yield return new TestCaseData(new DbUpdateException("save failed",
            new NpgsqlException("command timed out", new TimeoutException())));
        yield return new TestCaseData(new InvalidOperationException("provider execution strategy failure",
            new NpgsqlException("transport failed", new IOException())));
        yield return new TestCaseData(new InvalidOperationException("provider execution strategy failure",
            new DbUpdateException("save failed", CreatePostgresException("08006"))));
    }

    [TestCaseSource(nameof(AvailabilityFailures))]
    public async Task SaveChangesAsync_OnKnownAvailabilityFailure_ShouldReturnDependencyUnavailable(Exception exception)
    {
        await using var context = CreateContext(new ThrowingSaveInterceptor(exception));

        var result = await new EfCoreUnitOfWork(context).SaveChangesAsync();

        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
    }

    private static IEnumerable<TestCaseData> UnexpectedFailures()
    {
        foreach (var sqlState in new[] { "28P01", "42501", "42601", "40001", "40P01", "57014", "XX000" })
        {
            yield return new TestCaseData(new DbUpdateException("save failed", CreatePostgresException(sqlState)))
                .SetName($"SaveChangesAsync_OnUnexpectedPostgreSql_{sqlState}_ShouldRethrow");
        }

        yield return new TestCaseData(new OperationCanceledException());
        yield return new TestCaseData(new TaskCanceledException());
        yield return new TestCaseData(new NpgsqlException("cancelled", new OperationCanceledException()));
        yield return new TestCaseData(new NpgsqlException("transport failed",
            new IOException("cancelled transport", new OperationCanceledException())));
        yield return new TestCaseData(new InvalidOperationException("provider execution strategy failure",
            new NpgsqlException("transport failed",
                new IOException("cancelled transport", new OperationCanceledException()))));
        yield return new TestCaseData(new NpgsqlException("unknown provider error"));
        yield return new TestCaseData(new IOException("application failure"));
        yield return new TestCaseData(new TimeoutException("application failure"));
        yield return new TestCaseData(new InvalidOperationException("unexpected", new TimeoutException()));
    }

    [TestCaseSource(nameof(UnexpectedFailures))]
    public void SaveChangesAsync_OnCancellationOrUnexpectedFailure_ShouldRethrowSameException(Exception exception)
    {
        using var context = CreateContext(new ThrowingSaveInterceptor(exception));

        var thrown = Assert.CatchAsync(async () => await new EfCoreUnitOfWork(context).SaveChangesAsync());

        Assert.That(thrown, Is.SameAs(exception));
    }

    [Test]
    public void SaveChangesAsync_ShouldForwardCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var interceptor = new ThrowingSaveInterceptor(new OperationCanceledException());
        using var context = CreateContext(interceptor);

        Assert.CatchAsync(async () =>
            await new EfCoreUnitOfWork(context).SaveChangesAsync(cancellationSource.Token));

        Assert.That(interceptor.CapturedToken, Is.EqualTo(cancellationSource.Token));
    }

    private static AppDbContext CreateContext(ThrowingSaveInterceptor interceptor) => new(
        PersistenceProbeModel.CreateOptions("Host=localhost;Database=probe;Username=probe")
            .AddInterceptors(interceptor).Options);

    private static PostgresException CreatePostgresException(string sqlState, string? constraintName = null) =>
        new("test provider failure", "ERROR", "ERROR", sqlState, constraintName: constraintName);

    private sealed class ThrowingSaveInterceptor(Exception exception) : SaveChangesInterceptor
    {
        public CancellationToken CapturedToken { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            throw exception;
        }
    }
}
