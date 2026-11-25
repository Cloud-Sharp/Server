using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Time;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Idempotency;

/// <summary>한 요청에서 멱등성을 적용할 업무 트랜잭션의 선점과 복구 정책을 보관한다.</summary>
public sealed class IdempotencyTransactionContext : IIdempotencyTransactionContext
{
    private Transaction? _active;

    public bool CanReleaseReservation => _active?.CanReleaseReservation == true;

    public IDisposable Activate(
        IdempotencyReservation reservation,
        int resultVersion,
        Type transactionResultType,
        Func<object?, IdempotencyRecovery> createRecovery,
        IIdempotencyStore store,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(transactionResultType);
        ArgumentNullException.ThrowIfNull(createRecovery);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resultVersion);

        if (transactionResultType == typeof(void))
        {
            throw new ArgumentException("An idempotent operation must return a typed transaction result.", nameof(transactionResultType));
        }

        if (_active is not null)
        {
            throw new InvalidOperationException("The request already has an active idempotency transaction context.");
        }

        var transaction = new Transaction(reservation, resultVersion, transactionResultType, createRecovery, store, clock);
        _active = transaction;
        return new Activation(this, transaction);
    }

    internal Transaction? GetTransaction(Type resultType)
    {
        if (_active is not null && _active.ResultType != resultType)
        {
            throw new InvalidOperationException("The transaction result type does not match the active idempotency policy.");
        }

        return _active;
    }

    private sealed class Activation(IdempotencyTransactionContext context, Transaction transaction) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(context._active, transaction))
            {
                context._active = null;
            }
        }
    }

    internal sealed class Transaction(
        IdempotencyReservation reservation,
        int resultVersion,
        Type resultType,
        Func<object?, IdempotencyRecovery> createRecovery,
        IIdempotencyStore store,
        IClock clock)
    {
        private bool _canRelease = true;
        private bool _commitPending;

        internal Type ResultType { get; } = resultType;
        internal bool CanReleaseReservation => _canRelease && !_commitPending;

        internal async Task<Result> PrepareAsync(CancellationToken cancellationToken)
        {
            _canRelease = false;

            var request = reservation.Request;
            var result = await store.LockAsync(request.Scope, request.Key, cancellationToken);
            if (result.IsFailed)
            {
                return Result.Fail(result.Errors);
            }

            var row = result.Value;
            var now = clock.UtcNow;
            if (row is IdempotencyRecord.Completed completed && Matches(completed, now))
            {
                MarkCommitted();
                throw new IdempotencyCompletionRecoveredException(completed);
            }

            if (row is not IdempotencyRecord.InProgress pending ||
                pending.Reservation != reservation || pending.LeaseExpiresAt <= now)
            {
                return Result.Fail(CommonError.RequestConflict());
            }

            // The locked pending row proves that any previous commit attempt did not complete.
            _commitPending = false;
            return Result.Ok();
        }

        internal Task<Result> SaveCompletionAsync(object? result, CancellationToken cancellationToken)
        {
            var recovery = createRecovery(result)
                ?? throw new InvalidOperationException("A successful idempotent operation must provide recovery data.");
            var completion = new IdempotencyRecord.Completed(reservation, resultVersion, recovery, clock.UtcNow);
            return store.CompleteAsync(completion, cancellationToken);
        }

        internal void MarkRolledBack() => _canRelease = true;

        internal void MarkCommitPending() => _commitPending = true;

        internal void MarkCommitted()
        {
            _commitPending = false;
            _canRelease = false;
        }

        internal async Task RecoverAfterCommitFailureAsync(CancellationToken cancellationToken)
        {
            if (!_commitPending)
            {
                return;
            }

            var result = await store.FindCompletionAsync(reservation, cancellationToken);
            if (result.IsSuccess && result.Value is { } completion && Matches(completion, clock.UtcNow))
            {
                MarkCommitted();
                throw new IdempotencyCompletionRecoveredException(completion);
            }
        }

        private bool Matches(IdempotencyRecord.Completed completion, DateTimeOffset now) =>
            completion.Reservation == reservation && completion.ExpiresAt > now;
    }
}
