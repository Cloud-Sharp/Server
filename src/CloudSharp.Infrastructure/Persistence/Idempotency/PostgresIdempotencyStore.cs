using System.Security.Cryptography;
using System.Text;
using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Dapper;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Idempotency;

internal sealed partial class PostgresIdempotencyStore(
    AppDbContext dbContext,
    DapperDbSession session,
    ITransactionExecutor transactionExecutor,
    IdempotencyRecoverySerializer serializer) : IIdempotencyStore
{
    private const string InProgress = "IN_PROGRESS";
    private const string Completed = "COMPLETED";
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(1);

    public Task<Result<IdempotencyRecord>> ReserveOrGetAsync(
        IdempotencyReservation candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return transactionExecutor.ExecuteAsync(
            ct => ReserveInTransactionAsync(candidate, now, ct),
            cancellationToken: cancellationToken);
    }

    public Task<Result<IdempotencyRecord?>> LockAsync(
        IdempotencyScope scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        var rowKey = RowKey.Create(scope, key);
        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                EnsureTransaction(context, required: true);
                var row = await ReadAsync(rowKey, " FOR UPDATE", ct);
                return row is null ? null : ToRecord(row);
            },
            cancellationToken);
    }

    public async Task<Result> CompleteAsync(
        IdempotencyRecord.Completed completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTransaction(dbContext, required: true);

        var reservation = completion.Reservation;
        var request = reservation.Request;
        var scope = request.Scope;
        var recovery = serializer.Serialize(request.OperationId, completion.ResultVersion, completion.Recovery);
        var affected = await ExecuteChangeAsync(CompleteSql, new
        {
            ScopeHash = GetScopeHash(scope),
            scope.Actor,
            scope.Method,
            scope.CanonicalPath,
            request.Key,
            request.Fingerprint,
            request.OperationId,
            reservation.AttemptId,
            completion.ResultVersion,
            RecoveryKind = recovery.Kind,
            RecoveryPayload = recovery.Payload,
            CompletedAt = completion.CompletedAt.UtcDateTime,
            ExpiresAt = completion.ExpiresAt.UtcDateTime,
        }, cancellationToken);

        if (affected.IsFailed)
        {
            return Result.Fail(affected.Errors);
        }

        return affected.Value == 1 ? Result.Ok() : Result.Fail(CommonError.RequestConflict());
    }

    public Task<Result<IdempotencyRecord.Completed?>> FindCompletionAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var request = reservation.Request;
        var rowKey = RowKey.Create(request.Scope, request.Key);

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                EnsureTransaction(context, required: false);
                var row = await ReadAsync(rowKey, string.Empty, ct);
                return row is { State: Completed } && ToReservation(row) == reservation
                    ? ToCompletion(row, reservation)
                    : null;
            },
            cancellationToken);
    }

    public async Task<Result<bool>> ReleaseAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureTransaction(dbContext, required: false);

        var request = reservation.Request;
        var scope = request.Scope;
        var affected = await ExecuteChangeAsync(ReleaseSql, new
        {
            ScopeHash = GetScopeHash(scope),
            scope.Actor,
            scope.Method,
            scope.CanonicalPath,
            request.Key,
            request.Fingerprint,
            request.OperationId,
            reservation.AttemptId,
        }, cancellationToken);

        return affected.IsSuccess ? Result.Ok(affected.Value == 1) : Result.Fail<bool>(affected.Errors);
    }

    private async Task<Result<IdempotencyRecord>> ReserveInTransactionAsync(
        IdempotencyReservation candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var request = candidate.Request;
        var scope = request.Scope;
        var rowKey = RowKey.Create(scope, request.Key);
        var leaseExpiresAt = now.ToUniversalTime().Add(ReservationLease);
        var parameters = new
        {
            rowKey.ScopeHash,
            rowKey.Key,
            scope.Actor,
            scope.Method,
            scope.CanonicalPath,
            request.Fingerprint,
            request.OperationId,
            candidate.AttemptId,
            LeaseExpiresAt = leaseExpiresAt.UtcDateTime,
        };

        while (true)
        {
            var current = await ReadAsync(rowKey, string.Empty, cancellationToken);
            if (current is null)
            {
                var insert = session.CreateCommand(InsertSql, parameters, cancellationToken: cancellationToken);
                if (await session.Connection.ExecuteAsync(insert) == 1)
                {
                    return Result.Ok<IdempotencyRecord>(new IdempotencyRecord.InProgress(candidate, leaseExpiresAt));
                }

                // A competing insert may have committed after the initial read.
                continue;
            }

            if (!CanReserve(current, request, now.UtcDateTime))
            {
                return ToResultForRequest(current, request);
            }

            // A committed completion can only be locked by a reader or a competing reservation.
            // Never wait on a live business transaction which still owns an expired lease.
            var expiredLease = current.State == InProgress;
            var lockClause = expiredLease ? " FOR UPDATE SKIP LOCKED" : " FOR UPDATE";
            var locked = await ReadAsync(rowKey, lockClause, cancellationToken);
            if (locked is null)
            {
                if (expiredLease)
                {
                    return ToResultForRequest(current, request);
                }

                continue;
            }

            if (!CanReserve(locked, request, now.UtcDateTime))
            {
                return ToResultForRequest(locked, request);
            }

            var replace = session.CreateCommand(ReplaceSql, parameters, cancellationToken: cancellationToken);
            if (await session.Connection.ExecuteAsync(replace) != 1)
            {
                throw new InvalidOperationException("A locked idempotency reservation could not be replaced.");
            }

            return Result.Ok<IdempotencyRecord>(new IdempotencyRecord.InProgress(candidate, leaseExpiresAt));
        }
    }

    private async Task<IdempotencyRequestEntity?> ReadAsync(
        RowKey rowKey,
        string lockClause,
        CancellationToken cancellationToken)
    {
        var command = session.CreateCommand(
            SelectSql + lockClause,
            new { rowKey.ScopeHash, rowKey.Key },
            cancellationToken: cancellationToken);
        var row = await session.Connection.QuerySingleOrDefaultAsync<IdempotencyRequestEntity>(command);
        if (row is not null &&
            (row.Actor != rowKey.Scope.Actor || row.Method != rowKey.Scope.Method ||
             row.CanonicalPath != rowKey.Scope.CanonicalPath || row.Key != rowKey.Key))
        {
            throw new InvalidOperationException("The persisted idempotency scope does not match the request.");
        }

        return row;
    }

    private async Task<Result<int>> ExecuteChangeAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var command = session.CreateCommand(sql, parameters, cancellationToken: cancellationToken);
            return Result.Ok(await session.Connection.ExecuteAsync(command));
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<int>(CommonError.DependencyUnavailable());
        }
    }

    private static bool CanReserve(IdempotencyRequestEntity row, IdempotencyRequestIdentity request, DateTime now) =>
        row.State == Completed
            ? row.ExpiresAt <= now
            : row.LeaseExpiresAt <= now && MatchesInput(row, request);

    private static bool MatchesInput(IdempotencyRequestEntity row, IdempotencyRequestIdentity request) =>
        row.Fingerprint == request.Fingerprint && row.OperationId == request.OperationId;

    private Result<IdempotencyRecord> ToResultForRequest(IdempotencyRequestEntity row, IdempotencyRequestIdentity request) =>
        MatchesInput(row, request)
            ? Result.Ok(ToRecord(row))
            : Result.Fail<IdempotencyRecord>(IdempotencyError.KeyReused());

    private IdempotencyRecord ToRecord(IdempotencyRequestEntity row)
    {
        var reservation = ToReservation(row);
        return row.State switch
        {
            InProgress when row.LeaseExpiresAt is { } leaseExpiresAt =>
                new IdempotencyRecord.InProgress(reservation, new DateTimeOffset(leaseExpiresAt)),
            Completed => ToCompletion(row, reservation),
            _ => throw new InvalidOperationException("The persisted idempotency state is invalid."),
        };
    }

    private IdempotencyRecord.Completed ToCompletion(
        IdempotencyRequestEntity row,
        IdempotencyReservation reservation)
    {
        if (row is not
            { ResultVersion: { } version, RecoveryKind: { } kind, RecoveryPayload: { } payload, CompletedAt: { } completedAt })
        {
            throw new InvalidOperationException("The persisted idempotency completion is incomplete.");
        }

        var recovery = serializer.Deserialize(row.OperationId, version, kind, payload);
        return new IdempotencyRecord.Completed(reservation, version, recovery, new DateTimeOffset(completedAt));
    }

    private static IdempotencyReservation ToReservation(IdempotencyRequestEntity row) =>
        new(new IdempotencyRequestIdentity(
            new IdempotencyScope(row.Actor, row.Method, row.CanonicalPath),
            row.Key,
            row.Fingerprint,
            row.OperationId), row.AttemptId);

    private static byte[] GetScopeHash(IdempotencyScope scope)
    {
        // Lengths delimit the separate fields, including when canonical paths contain punctuation.
        var identity = FormattableString.Invariant(
            $"{scope.Actor.Length}:{scope.Actor}{scope.Method.Length}:{scope.Method}{scope.CanonicalPath}");
        return SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    }

    private static void EnsureTransaction(AppDbContext context, bool required)
    {
        if ((context.Database.CurrentTransaction is not null) != required)
        {
            throw new InvalidOperationException(required
                ? "This idempotency operation requires an active business transaction."
                : "This idempotency operation must run outside the business transaction.");
        }
    }

    private sealed record RowKey(IdempotencyScope Scope, byte[] ScopeHash, string Key)
    {
        public static RowKey Create(IdempotencyScope scope, string key) => new(scope, GetScopeHash(scope), key);
    }
}
