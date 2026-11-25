using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using FluentResults;

namespace CloudSharp.Api.Tests.TestSupport;

internal sealed class ApiTestIdempotencyStore : IIdempotencyStore
{
    private IdempotencyRecord? _record;

    public Task<Result<IdempotencyRecord>> ReserveOrGetAsync(
        IdempotencyReservation candidate, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var record = _record ??= new IdempotencyRecord.InProgress(candidate, now.AddMinutes(1));
        return Task.FromResult(Result.Ok(record));
    }

    public Task<Result<IdempotencyRecord?>> LockAsync(
        IdempotencyScope scope, string key, CancellationToken cancellationToken = default)
    {
        var request = _record?.Reservation.Request;
        return Task.FromResult(Result.Ok(request is not null && request.Scope == scope && request.Key == key ? _record : null));
    }

    public Task<Result> CompleteAsync(
        IdempotencyRecord.Completed completion, CancellationToken cancellationToken = default)
    {
        if (_record is not IdempotencyRecord.InProgress started || started.Reservation != completion.Reservation)
        {
            return Task.FromResult(Result.Fail(CommonError.RequestConflict()));
        }

        _record = completion;
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<IdempotencyRecord.Completed?>> FindCompletionAsync(
        IdempotencyReservation reservation, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Ok(_record is IdempotencyRecord.Completed completed && completed.Reservation == reservation
            ? completed
            : null));

    public Task<Result<bool>> ReleaseAsync(
        IdempotencyReservation reservation, CancellationToken cancellationToken = default)
    {
        if (_record is not IdempotencyRecord.InProgress started || started.Reservation != reservation)
        {
            return Task.FromResult(Result.Ok(false));
        }

        _record = null;
        return Task.FromResult(Result.Ok(true));
    }
}
