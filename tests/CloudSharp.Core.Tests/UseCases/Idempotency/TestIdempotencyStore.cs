using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Idempotency;
using FluentResults;

namespace CloudSharp.Core.Tests.UseCases.Idempotency;

internal sealed class TestIdempotencyStore : IIdempotencyStore
{
    public IdempotencyRecord? Record { get; set; }
    public int ReservationRequests { get; private set; }
    public int ReleaseRequests { get; private set; }

    public Task<Result<IdempotencyRecord>> ReserveOrGetAsync(
        IdempotencyReservation candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ReservationRequests++;
        var record = Record ??= new IdempotencyRecord.InProgress(candidate, now.AddMinutes(1));
        return Task.FromResult(Result.Ok(record));
    }

    public Task<Result<bool>> ReleaseAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ReleaseRequests++;
        if (Record is not IdempotencyRecord.InProgress inProgress || inProgress.Reservation != reservation)
        {
            return Task.FromResult(Result.Ok(false));
        }

        Record = null;
        return Task.FromResult(Result.Ok(true));
    }

    public Task<Result<IdempotencyRecord?>> LockAsync(
        IdempotencyScope scope,
        string key,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Result> CompleteAsync(
        IdempotencyRecord.Completed completion,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Result<IdempotencyRecord.Completed?>> FindCompletionAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
