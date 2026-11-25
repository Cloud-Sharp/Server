using System.Data;
using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.UseCases.Idempotency.Execution;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Idempotency.Execution;

[TestFixture]
public sealed class IdempotentRequestExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid PublicId = Guid.Parse("0194bdeb-48dc-72b4-ac38-7b4c738dc936");
    private static readonly IdempotencyRequestIdentity Request = new(
        new IdempotencyScope("account-42", "POST", "/api/v2/example-jobs"),
        "example-job-request-42",
        new string('a', 64),
        "submit-example-job");
    private static readonly IdempotencyReservation Reservation = new(
        Request, Guid.Parse("8aec0d56-1c11-4708-9380-d47abdb511f9"));

    [Test]
    public async Task ExecuteAsync_ShouldCommitOnlyProjectedRecoveryData()
    {
        var harness = new Harness();
        var businessResult = new BusinessResult(PublicId, "one-time-value");

        var result = await harness.CreateExecutor().ExecuteAsync(
            Reservation,
            2,
            _ =>
            {
                harness.Clock.Advance(TimeSpan.FromMinutes(2));
                return Task.FromResult(Result.Ok(businessResult));
            },
            value => new IdempotencyRecovery.SecretNotReplayable(value.PublicId));

        var expectedCompletion = new IdempotencyRecord.Completed(
            Reservation, 2, new IdempotencyRecovery.SecretNotReplayable(PublicId), Now.AddMinutes(2));
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(new IdempotentExecutionResult<BusinessResult>.Executed(businessResult)));
            Assert.That(harness.Store.Persisted, Is.EqualTo(expectedCompletion));
            Assert.That(harness.Transactions.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollBackAndReleaseReservationAfterCompletionFailure()
    {
        var harness = new Harness();
        harness.Store.CompletionFailure = CommonError.DependencyUnavailable();

        var result = await harness.CreateExecutor().ExecuteAsync(
            Reservation, 1, harness.ExecuteBusinessAsync, Project, retryRolledBackOperation: _ => true);

        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.ExecutionCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleasedReservation, Is.EqualTo(Reservation));
            Assert.That(harness.Store.Persisted, Is.Null);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRetryRolledBackBusinessOperationWithoutReleasingReservation()
    {
        var harness = new Harness();
        var releaseCounts = new List<int>();

        var result = await harness.CreateExecutor().ExecuteAsync(
            Reservation,
            1,
            ct =>
            {
                releaseCounts.Add(harness.Store.ReleaseCount);
                return releaseCounts.Count == 1
                    ? Task.FromResult(Result.Fail<BusinessResult>(CommonError.PreconditionFailed()))
                    : harness.ExecuteBusinessAsync(ct);
            },
            Project,
            retryRolledBackOperation: _ => true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.TypeOf<IdempotentExecutionResult<BusinessResult>.Executed>());
            Assert.That(releaseCounts, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReleaseReservationAfterRetriedOperationFails()
    {
        var harness = new Harness();
        var releaseCounts = new List<int>();

        var result = await harness.CreateExecutor().ExecuteAsync(
            Reservation,
            1,
            _ =>
            {
                releaseCounts.Add(harness.Store.ReleaseCount);
                return Task.FromResult(Result.Fail<BusinessResult>(CommonError.PreconditionFailed()));
            },
            Project,
            retryRolledBackOperation: _ => true);

        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
            Assert.That(releaseCounts, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(2));
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Store.ReleaseCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleasedReservation, Is.EqualTo(Reservation));
            Assert.That(harness.Store.Persisted, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldRejectAnAttemptReplacedByAnotherReservation(bool replacementCompleted)
    {
        var replacement = Reservation with { AttemptId = Guid.Parse("306a6be2-e062-4a31-a459-4f8202c73c0b") };
        IdempotencyRecord record = replacementCompleted
            ? new IdempotencyRecord.Completed(replacement, 1, new IdempotencyRecovery.Replayable<Guid>(PublicId), Now)
            : new IdempotencyRecord.InProgress(replacement, Now.AddMinutes(1));
        var harness = new Harness(record);

        var result = await harness.CreateExecutor().ExecuteAsync(Reservation, 1, harness.ExecuteBusinessAsync, Project);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(harness.ExecutionCount, Is.Zero);
            Assert.That(harness.Store.ReleaseCount, Is.Zero);
            Assert.That(harness.Store.Persisted, Is.EqualTo(record));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldRecoverCommittedResultOnce(bool retryTransactionCallback)
    {
        var harness = new Harness();
        var transactionExecutor = new LostCommitAcknowledgement(harness.Transactions, retryTransactionCallback);

        var result = await harness.CreateExecutor(transactionExecutor)
            .ExecuteAsync(Reservation, 1, harness.ExecuteBusinessAsync, Project);

        var expectedCompletion = new IdempotencyRecord.Completed(
            Reservation, 1, new IdempotencyRecovery.Replayable<Guid>(PublicId), Now);
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(new IdempotentExecutionResult<BusinessResult>.Recovered(expectedCompletion)));
            Assert.That(harness.Store.Persisted, Is.EqualTo(expectedCompletion));
            Assert.That(harness.Store.CompletionWriteCount, Is.EqualTo(1));
            Assert.That(harness.ExecutionCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldKeepReservationWithoutProofOfCommitOrRollback()
    {
        var harness = new Harness();
        harness.Transactions.CommitFailure = CommonError.DependencyUnavailable();
        var originalRecord = harness.Store.Persisted;

        var result = await harness.CreateExecutor().ExecuteAsync(Reservation, 1, harness.ExecuteBusinessAsync, Project);

        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            Assert.That(harness.Store.CompletionWriteCount, Is.EqualTo(1));
            Assert.That(harness.Store.ReleaseCount, Is.Zero);
            Assert.That(harness.Store.Persisted, Is.EqualTo(originalRecord));
        });
    }

    private static IdempotencyRecovery Project(BusinessResult result) =>
        new IdempotencyRecovery.Replayable<Guid>(result.PublicId);

    private sealed record BusinessResult(Guid PublicId, string OneTimeSecret);

    private sealed class Harness
    {
        public Harness(IdempotencyRecord? initialRecord = null)
        {
            var session = new FakePersistenceSession();
            Store = new Store(session, initialRecord ?? new IdempotencyRecord.InProgress(Reservation, Now.AddMinutes(1)));
            Transactions = new FakeTransactionExecutor(session) { OnCommitted = Store.PublishCompletion };
        }

        public FakeClock Clock { get; } = new(Now);
        public Store Store { get; }
        public FakeTransactionExecutor Transactions { get; }
        public int ExecutionCount { get; private set; }

        public IdempotentRequestExecutor CreateExecutor(ITransactionExecutor? transactionExecutor = null) =>
            new(Store, transactionExecutor ?? Transactions, Clock);

        public Task<Result<BusinessResult>> ExecuteBusinessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            return Task.FromResult(Result.Ok(new BusinessResult(PublicId, "one-time-value")));
        }
    }

    private sealed class Store(FakePersistenceSession session, IdempotencyRecord initialRecord) : IIdempotencyStore
    {
        private IdempotencyRecord.Completed? pendingCompletion;

        public IdempotencyRecord? Persisted { get; private set; } = initialRecord;
        public IError? CompletionFailure { get; set; }
        public IdempotencyReservation? ReleasedReservation { get; private set; }
        public int ReleaseCount { get; private set; }
        public int CompletionWriteCount { get; private set; }

        public Task<Result<IdempotencyRecord>> ReserveOrGetAsync(
            IdempotencyReservation candidate,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<IdempotencyRecord?>> LockAsync(
            IdempotencyScope scope, string key, CancellationToken cancellationToken = default)
        {
            Assert.That(session.IsTransactionActive, Is.True);
            return Task.FromResult(Result.Ok<IdempotencyRecord?>(Persisted));
        }

        public Task<Result> CompleteAsync(
            IdempotencyRecord.Completed completion, CancellationToken cancellationToken = default)
        {
            Assert.That(session.IsTransactionActive, Is.True);
            CompletionWriteCount++;
            if (CompletionFailure is { } failure)
            {
                return Task.FromResult(Result.Fail(failure));
            }

            pendingCompletion = completion;
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<IdempotencyRecord.Completed?>> FindCompletionAsync(
            IdempotencyReservation reservation, CancellationToken cancellationToken = default)
        {
            Assert.That(session.IsTransactionActive, Is.False);
            return Task.FromResult(Result.Ok<IdempotencyRecord.Completed?>(Persisted as IdempotencyRecord.Completed));
        }

        public Task<Result<bool>> ReleaseAsync(
            IdempotencyReservation reservation, CancellationToken cancellationToken = default)
        {
            Assert.That(session.IsTransactionActive, Is.False);
            ReleaseCount++;
            ReleasedReservation = reservation;
            var released = Persisted is IdempotencyRecord.InProgress current && current.Reservation == reservation;
            if (released)
            {
                Persisted = null;
            }

            return Task.FromResult(Result.Ok(released));
        }

        public void PublishCompletion()
        {
            if (pendingCompletion is not { } completion)
            {
                return;
            }

            Persisted = completion;
            pendingCompletion = null;
        }
    }

    private sealed class LostCommitAcknowledgement(FakeTransactionExecutor transactionExecutor, bool retryCallback)
        : ITransactionExecutor
    {
        public Task<Result> ExecuteAsync(
            Func<CancellationToken, Task<Result>> operation,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<Result<T>> ExecuteAsync<T>(
            Func<CancellationToken, Task<Result<T>>> operation,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
        {
            var firstResult = await transactionExecutor.ExecuteAsync(operation, isolationLevel, cancellationToken);
            if (firstResult.IsFailed)
            {
                return firstResult;
            }

            return retryCallback
                ? await transactionExecutor.ExecuteAsync(operation, isolationLevel, cancellationToken)
                : Result.Fail<T>(CommonError.DependencyUnavailable());
        }
    }
}
