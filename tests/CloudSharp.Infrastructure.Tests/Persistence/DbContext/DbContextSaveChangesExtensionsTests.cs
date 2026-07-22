using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentAssertions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NUnit.Framework;
using Npgsql;

namespace CloudSharp.Infrastructure.Tests.Persistence.DbContext;

[TestFixture]
public sealed class DbContextSaveChangesExtensionsTests
{
    private static readonly IReadOnlyList<IUpdateEntry> EmptyEntries = Array.Empty<IUpdateEntry>();

    private static PostgresException CreatePostgresException(string sqlState, string? constraintName)
    {
        if (constraintName is null)
        {
            return new PostgresException(
                "test message",
                "ERROR",
                "ERROR",
                sqlState);
        }

        return new PostgresException(
            "test message",
            "ERROR",
            "ERROR",
            sqlState,
            "detail",
            "hint",
            0,
            0,
            "internalQuery",
            "where",
            "schema",
            "table",
            "column",
            "dataType",
            constraintName,
            "file",
            "line",
            "routine");
    }

    private static DbUpdateException CreateDbUpdateException(Exception inner) =>
        new("Save failed", inner, EmptyEntries);

    private static DbUpdateConcurrencyException CreateConcurrencyException(Exception? inner) =>
        inner is null
            ? new DbUpdateConcurrencyException("Concurrency", EmptyEntries)
            : new DbUpdateConcurrencyException("Concurrency", inner, EmptyEntries);

    [Test]
    public async Task TrySaveChangesAsync_OnSuccess_ShouldReturnRowsAffected()
    {
        var fake = new FakeDbContext { RowsAffected = 7 };

        var result = await fake.TrySaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [TestCase("23505", nameof(DbConstraintKind.Unique), "uq_space_slug")]
    [TestCase("23503", nameof(DbConstraintKind.ForeignKey), "fk_member_space")]
    [TestCase("23502", nameof(DbConstraintKind.NotNull), "not_null_member_email")]
    [TestCase("23514", nameof(DbConstraintKind.Check), "chk_member_quota")]
    [TestCase("23P01", nameof(DbConstraintKind.Exclusion), "ex_overlap_range")]
    public async Task TrySaveChangesAsync_OnClass23Error_ShouldMapToConstraintError(
        string sqlState, string expectedKindName, string constraintName)
    {
        var expectedKind = Enum.Parse<DbConstraintKind>(expectedKindName);
        var pgEx = CreatePostgresException(sqlState, constraintName);
        var dbEx = CreateDbUpdateException(pgEx);
        var fake = new FakeDbContext { ExceptionToThrow = dbEx };

        var result = await fake.TrySaveChangesAsync();

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.OfType<DbConstraintViolationError>().Single();
        error.Kind.Should().Be(expectedKind);
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraintName);
        error.OriginalException.Should().BeSameAs(dbEx);

        error.Message.Should().NotContain(sqlState);
        error.Message.Should().NotContain(constraintName);
        error.Message.Should().NotContain("test message");
        error.Message.Should().NotContain("table");
        error.Metadata.Should().BeEmpty();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnUnknownClass23Code_ShouldClassifyAsOtherIntegrityConstraint()
    {
        var pgEx = CreatePostgresException("23000", null);
        var dbEx = CreateDbUpdateException(pgEx);
        var fake = new FakeDbContext { ExceptionToThrow = dbEx };

        var result = await fake.TrySaveChangesAsync();

        var error = result.Errors.OfType<DbConstraintViolationError>().Single();
        error.Kind.Should().Be(DbConstraintKind.OtherIntegrityConstraint);
        error.SqlState.Should().Be("23000");
    }

    [Test]
    public async Task TrySaveChangesAsync_OnClass23WithoutConstraintName_ShouldPreserveNullConstraintName()
    {
        var pgEx = CreatePostgresException("23505", null);
        var dbEx = CreateDbUpdateException(pgEx);
        var fake = new FakeDbContext { ExceptionToThrow = dbEx };

        var result = await fake.TrySaveChangesAsync();

        var error = result.Errors.OfType<DbConstraintViolationError>().Single();
        error.Kind.Should().Be(DbConstraintKind.Unique);
        error.SqlState.Should().Be("23505");
        error.ConstraintName.Should().BeNull();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnNonClass23PostgresException_ShouldRethrow()
    {
        var pgEx = CreatePostgresException("40001", null);
        var dbEx = CreateDbUpdateException(pgEx);
        var fake = new FakeDbContext { ExceptionToThrow = dbEx };

        var act = async () => await fake.TrySaveChangesAsync();

        var thrown = await act.Should().ThrowAsync<DbUpdateException>();
        thrown.Subject.First().InnerException.Should().BeOfType<PostgresException>();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnDbUpdateExceptionWithoutPostgresInner_ShouldRethrow()
    {
        var dbEx = new DbUpdateException("Save failed", new InvalidOperationException("boom"), EmptyEntries);
        var fake = new FakeDbContext { ExceptionToThrow = dbEx };

        var act = async () => await fake.TrySaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnDbUpdateConcurrencyException_ShouldRethrow()
    {
        var concurrencyEx = CreateConcurrencyException(null);
        var fake = new FakeDbContext { ExceptionToThrow = concurrencyEx };

        var act = async () => await fake.TrySaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnDbUpdateConcurrencyExceptionWithClass23Inner_ShouldStillRethrow()
    {
        var pgEx = CreatePostgresException("23505", "uq_x");
        var concurrencyEx = CreateConcurrencyException(pgEx);
        var fake = new FakeDbContext { ExceptionToThrow = concurrencyEx };

        var act = async () => await fake.TrySaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task TrySaveChangesAsync_OnOperationCanceledException_ShouldRethrow()
    {
        var fake = new FakeDbContext { ExceptionToThrow = new OperationCanceledException() };

        var act = async () => await fake.TrySaveChangesAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task TrySaveChangesAsync_ShouldForwardCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var fake = new FakeDbContext();

        await fake.TrySaveChangesAsync(token);

        fake.CapturedToken.Should().Be(token);
    }

    [Test]
    public async Task TrySaveChangesAsync_OnNullContext_ShouldThrowArgumentNullException()
    {
        Microsoft.EntityFrameworkCore.DbContext? nullContext = null;

        var act = async () => await nullContext!.TrySaveChangesAsync();

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("dbContext");
    }

    private sealed class FakeDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public int RowsAffected { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public CancellationToken CapturedToken { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(RowsAffected);
        }
    }
}