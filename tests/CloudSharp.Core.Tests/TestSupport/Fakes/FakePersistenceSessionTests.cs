using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.TestSupport.Fakes;

[TestFixture]
public sealed class FakePersistenceSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness()
        {
            Repository = new FakeUserRepository(Session);
            UnitOfWork = new FakeUnitOfWork(Session);
            Executor = new FakeTransactionExecutor(Session);
        }

        public FakePersistenceSession Session { get; } = new();
        public FakeUserRepository Repository { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor Executor { get; }
    }

    [Test]
    public async Task ExecuteAsync_ShouldExposeGeneratedIdentityOnlyAfterFlushAndCommitOnlyAfterSuccess()
    {
        // Arrange
        var harness = new Harness();
        var user = UserState();
        User? beforeFlush = null;
        User? afterFlush = null;
        var committedDuringOperation = -1;

        // Act
        var result = await harness.Executor.ExecuteAsync(async ct =>
        {
            await harness.Repository.AddAsync(user, ct);
            beforeFlush = (await harness.Repository.FindByPublicIdAsync(user.PublicId, ct)).Value;
            var saved = await harness.UnitOfWork.SaveChangesAsync(ct);
            afterFlush = (await harness.Repository.FindByPublicIdAsync(user.PublicId, ct)).Value;
            committedDuringOperation = harness.Session.PersistedUsersById.Count;
            return saved.ToResult();
        });

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(beforeFlush, Is.Null);
            Assert.That(afterFlush, Is.Not.Null);
            Assert.That(afterFlush!.Id, Is.EqualTo(harness.Session.AssignedId));
            Assert.That(afterFlush.PublicId, Is.EqualTo(user.PublicId));
            Assert.That(user.Id, Is.Zero);
            Assert.That(committedDuringOperation, Is.Zero);
            Assert.That(harness.Session.PersistedUsersById, Has.Count.EqualTo(1));
            Assert.That(harness.Executor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Session.IsTransactionActive, Is.False);
        });
    }

    [Test]
    public async Task FindByIdAsync_ShouldKeepSnapshotsIndependentAndSaveExplicitTargetAfterOtherLookups()
    {
        // Arrange
        var harness = new Harness();
        harness.Session.Seed(UserState(1));
        harness.Session.Seed(UserState(2));
        User? unchangedSnapshot = null;
        User? beforeFlush = null;
        User? afterFlush = null;

        // Act
        var result = await harness.Executor.ExecuteAsync(async ct =>
        {
            var first = (await harness.Repository.FindByIdAsync(1, ct)).Value!;
            first.ChangeProfile("Changed", Now.AddMinutes(1));
            unchangedSnapshot = (await harness.Repository.FindByIdAsync(1, ct)).Value;
            await harness.Repository.UpdateAsync(first, 1, ct);
            beforeFlush = (await harness.Repository.FindByIdAsync(1, ct)).Value;
            await harness.Repository.FindByIdAsync(2, ct);
            var saved = await harness.UnitOfWork.SaveChangesAsync(ct);
            afterFlush = (await harness.Repository.FindByIdAsync(1, ct)).Value;
            return saved.ToResult();
        });

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(unchangedSnapshot!.DisplayName, Is.EqualTo("Before"));
            Assert.That(beforeFlush!.DisplayName, Is.EqualTo("Before"));
            Assert.That(afterFlush!.DisplayName, Is.EqualTo("Changed"));
            Assert.That(harness.Session.PersistedUsersById[1].DisplayName, Is.EqualTo("Changed"));
            Assert.That(harness.Session.PersistedUsersById[2].DisplayName, Is.EqualTo("Before"));
            Assert.That(harness.Repository.UpdateCalls.Single().OriginalVersion, Is.EqualTo(1L));
        });
    }

    [TestCase("failure")]
    [TestCase("exception")]
    [TestCase("cancellation")]
    public async Task ExecuteAsync_ShouldDiscardFlushedChangesAfterUnsuccessfulOperation(string outcome)
    {
        // Arrange
        var harness = new Harness();
        var user = UserState();
        using var cancellation = new CancellationTokenSource();

        async Task<Result> Operation(CancellationToken ct)
        {
            await harness.Repository.AddAsync(user, ct);
            await harness.UnitOfWork.SaveChangesAsync(ct);
            if (outcome == "exception")
            {
                throw new InvalidOperationException("Operation failed after flush.");
            }

            if (outcome == "cancellation")
            {
                cancellation.Cancel();
                return Result.Ok();
            }

            return Result.Fail(CommonError.DependencyUnavailable());
        }

        // Act
        if (outcome == "exception")
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => harness.Executor.ExecuteAsync(Operation, cancellationToken: cancellation.Token));
        }
        else if (outcome == "cancellation")
        {
            Assert.ThrowsAsync<OperationCanceledException>(() => harness.Executor.ExecuteAsync(Operation, cancellationToken: cancellation.Token));
        }
        else
        {
            var result = await harness.Executor.ExecuteAsync(Operation);
            Assert.That(result.IsFailed, Is.True);
        }

        var reloaded = await harness.Repository.FindByPublicIdAsync(user.PublicId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Value, Is.Null);
            Assert.That(harness.Session.PersistedUsersById, Is.Empty);
            Assert.That(harness.Executor.CommitCount, Is.Zero);
            Assert.That(harness.Executor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Session.IsTransactionActive, Is.False);
        });
    }

    [Test]
    public async Task SaveChangesAsync_ShouldPreserveExternalCommitAfterConflictAndAllowFreshRetry()
    {
        // Arrange
        var harness = new Harness();
        var original = UserState(1);
        harness.Session.Seed(original);
        harness.UnitOfWork.BeforeSaveChanges = call =>
        {
            if (call == 1)
            {
                harness.Session.BumpStoredVersion(1);
            }
        };

        // Act
        var conflict = await harness.Executor.ExecuteAsync(async ct =>
        {
            var loaded = (await harness.Repository.FindByIdAsync(1, ct)).Value!;
            loaded.ChangePasswordHash("changed-hash", Now.AddMinutes(1));
            await harness.Repository.UpdateAsync(loaded, 1, ct);
            return (await harness.UnitOfWork.SaveChangesAsync(ct)).ToResult();
        });
        var afterConflict = harness.Session.PersistedUsersById[1];
        var retried = await harness.Executor.ExecuteAsync(async ct =>
        {
            var loaded = (await harness.Repository.FindByIdAsync(1, ct)).Value!;
            var expectedVersion = loaded.Version;
            loaded.ChangePasswordHash("retried-hash", Now.AddMinutes(2));
            await harness.Repository.UpdateAsync(loaded, expectedVersion, ct);
            return (await harness.UnitOfWork.SaveChangesAsync(ct)).ToResult();
        });

        // Assert
        Assert.That(conflict.IsFailed, Is.True);
        Assert.That(conflict.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        Assert.That(retried.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(afterConflict.PasswordHash, Is.EqualTo(original.PasswordHash));
            Assert.That(afterConflict.Version, Is.EqualTo(2L));
            Assert.That(afterConflict.SecurityVersion, Is.EqualTo(1L));
            Assert.That(harness.Session.PersistedUsersById[1].PasswordHash, Is.EqualTo("retried-hash"));
            Assert.That(harness.Session.PersistedUsersById[1].Version, Is.EqualTo(3L));
            Assert.That(harness.Repository.UpdateCalls.Select(call => call.OriginalVersion), Is.EqualTo(new[] { 1L, 2L }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldDiscardPendingChangesBeforeNextTransaction()
    {
        // Arrange
        var harness = new Harness();
        var user = UserState();
        var rowsInNextTransaction = -1;

        // Act
        var failed = await harness.Executor.ExecuteAsync(async ct =>
        {
            await harness.Repository.AddAsync(user, ct);
            return Result.Fail(CommonError.DependencyUnavailable());
        });
        var next = await harness.Executor.ExecuteAsync(async ct =>
        {
            var save = await harness.UnitOfWork.SaveChangesAsync(ct);
            rowsInNextTransaction = save.Value;
            return save.ToResult();
        });

        // Assert
        Assert.That(failed.IsFailed, Is.True);
        Assert.That(next.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rowsInNextTransaction, Is.Zero);
            Assert.That(harness.Session.PersistedUsersById, Is.Empty);
            Assert.That(harness.Executor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Executor.CommitCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SaveChangesAsync_ShouldFlushMultipleExplicitTargetsTogether()
    {
        // Arrange
        var harness = new Harness();
        harness.Session.Seed(UserState(1));
        harness.Session.Seed(UserState(2));
        var added = UserState();
        var affectedRows = -1;

        // Act
        var result = await harness.Executor.ExecuteAsync(async ct =>
        {
            foreach (var id in new[] { 1L, 2L })
            {
                var loaded = (await harness.Repository.FindByIdAsync(id, ct)).Value!;
                loaded.ChangeProfile($"Changed {id}", Now.AddMinutes(1));
                await harness.Repository.UpdateAsync(loaded, 1, ct);
            }

            await harness.Repository.AddAsync(added, ct);
            var saved = await harness.UnitOfWork.SaveChangesAsync(ct);
            affectedRows = saved.Value;
            return saved.ToResult();
        });

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(affectedRows, Is.EqualTo(3));
            Assert.That(harness.Session.PersistedUsersById, Has.Count.EqualTo(3));
            Assert.That(harness.Session.PersistedUsersById[1].DisplayName, Is.EqualTo("Changed 1"));
            Assert.That(harness.Session.PersistedUsersById[2].DisplayName, Is.EqualTo("Changed 2"));
            Assert.That(harness.Session.PersistedUsersById[harness.Session.AssignedId].PublicId,
                Is.EqualTo(added.PublicId));
            Assert.That(added.Id, Is.Zero);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task UpdateAsync_ShouldUseExplicitOriginalVersionAcrossDomainTransitions(int transitions)
    {
        // Arrange
        var harness = new Harness();
        harness.Session.Seed(UserState(1));

        // Act
        var result = await harness.Executor.ExecuteAsync(async ct =>
        {
            var loaded = (await harness.Repository.FindByIdAsync(1, ct)).Value!;
            var originalVersion = loaded.Version;
            for (var transition = 0; transition < transitions; transition++)
            {
                loaded.RevokeAllSessions(Now.AddMinutes(transition + 1));
            }

            await harness.Repository.UpdateAsync(loaded, originalVersion, ct);
            return (await harness.UnitOfWork.SaveChangesAsync(ct)).ToResult();
        });

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Repository.UpdateCalls.Single().OriginalVersion, Is.EqualTo(1L));
            Assert.That(harness.Session.PersistedUsersById[1].Version, Is.EqualTo(1L + transitions));
            Assert.That(harness.Session.PersistedUsersById[1].SecurityVersion, Is.EqualTo(1L + transitions));
        });
    }

    [Test]
    public async Task SaveChangesAsync_ShouldRejectWholeBatchWithoutPartialWrites()
    {
        // Arrange
        var harness = new Harness();
        harness.Session.Seed(UserState(1));
        harness.Session.Seed(UserState(2));
        var added = UserState();

        // Act
        var result = await harness.Executor.ExecuteAsync(async ct =>
        {
            foreach (var id in new[] { 1L, 2L })
            {
                var user = (await harness.Repository.FindByIdAsync(id, ct)).Value!;
                user.ChangeProfile("Changed", Now.AddMinutes(1));
                await harness.Repository.UpdateAsync(user, 1, ct);
            }

            await harness.Repository.AddAsync(added, ct);
            harness.Session.BumpStoredVersion(2);
            return (await harness.UnitOfWork.SaveChangesAsync(ct)).ToResult();
        });

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.PersistedUsersById, Has.Count.EqualTo(2));
            Assert.That(harness.Session.PersistedUsersById[1].DisplayName, Is.EqualTo("Before"));
            Assert.That(harness.Session.PersistedUsersById[1].Version, Is.EqualTo(1L));
            Assert.That(harness.Session.PersistedUsersById[2].DisplayName, Is.EqualTo("Before"));
            Assert.That(harness.Session.PersistedUsersById[2].Version, Is.EqualTo(2L));
        });
    }

    private static User UserState(long id = 0) => User.Reconstitute(
        id,
        Guid.Parse($"01920000-0000-7000-8000-{id + 1:000000000000}"),
        EmailAddress.Create($"user-{id}@example.com").Value,
        null,
        "Before",
        "original-hash",
        SystemRole.User,
        UserStatus.Active,
        1,
        1,
        Now,
        Now,
        null,
        null);
}
