using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Time;
using FluentResults;

namespace CloudSharp.Core.UseCases.Idempotency.Execution;

public sealed class IdempotentRequestExecutor(
    IIdempotencyStore store,
    ITransactionExecutor transactionExecutor,
    IClock clock)
{
    /// <summary>선점된 업무 변경과 그 복구 정보를 같은 트랜잭션에 저장한다.</summary>
    public async Task<Result<IdempotentExecutionResult<T>>> ExecuteAsync<T>(
        IdempotencyReservation reservation,
        int resultVersion,
        Func<CancellationToken, Task<Result<T>>> operation,
        Func<T, IdempotencyRecovery> createRecovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resultVersion);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(createRecovery);

        Result<IdempotentExecutionResult<T>>? operationFailure = null;
        var completionStaged = false;
        var result = await transactionExecutor.ExecuteAsync(ExecuteInTransactionAsync, cancellationToken: cancellationToken);

        // 기존 실행기는 명시적 롤백 후 콜백의 실패 결과를 그대로 반환한다.
        if (ReferenceEquals(result, operationFailure))
        {
            await store.ReleaseAsync(reservation, CancellationToken.None);
        }
        else if (result.IsFailed && completionStaged)
        {
            var completion = await store.FindCompletionAsync(reservation, cancellationToken);
            if (completion.IsSuccess && completion.Value is { } saved &&
                saved.Reservation == reservation && saved.ExpiresAt > clock.UtcNow)
            {
                return Result.Ok<IdempotentExecutionResult<T>>(new IdempotentExecutionResult<T>.Recovered(saved));
            }
        }

        return result;

        async Task<Result<IdempotentExecutionResult<T>>> ExecuteInTransactionAsync(CancellationToken transactionToken)
        {
            operationFailure = null;
            var request = reservation.Request;
            var current = await store.LockAsync(request.Scope, request.Key, transactionToken);
            if (current.IsFailed)
            {
                return Result.Fail<IdempotentExecutionResult<T>>(current.Errors);
            }

            var record = current.Value;
            if (record is null || record.Reservation != reservation)
            {
                return Result.Fail<IdempotentExecutionResult<T>>(CommonError.RequestConflict());
            }

            if (record is IdempotencyRecord.Completed completed && completed.ExpiresAt > clock.UtcNow)
            {
                return Result.Ok<IdempotentExecutionResult<T>>(new IdempotentExecutionResult<T>.Recovered(completed));
            }

            if (record is not IdempotencyRecord.InProgress inProgress || inProgress.LeaseExpiresAt <= clock.UtcNow)
            {
                return Result.Fail<IdempotentExecutionResult<T>>(CommonError.RequestConflict());
            }

            var businessResult = await operation(transactionToken);
            if (businessResult.IsFailed)
            {
                operationFailure = Result.Fail<IdempotentExecutionResult<T>>(businessResult.Errors);
                return operationFailure;
            }

            var recovery = createRecovery(businessResult.Value)
                ?? throw new InvalidOperationException("A successful operation must supply idempotency recovery data.");
            var completion = new IdempotencyRecord.Completed(reservation, resultVersion, recovery, clock.UtcNow);
            var saved = await store.CompleteAsync(completion, transactionToken);
            if (saved.IsFailed)
            {
                operationFailure = Result.Fail<IdempotentExecutionResult<T>>(saved.Errors);
                return operationFailure;
            }

            completionStaged = true;
            return Result.Ok<IdempotentExecutionResult<T>>(new IdempotentExecutionResult<T>.Executed(businessResult.Value));
        }
    }
}
