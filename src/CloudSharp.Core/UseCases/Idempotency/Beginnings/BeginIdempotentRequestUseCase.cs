using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Idempotency.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Idempotency.Beginnings;

public sealed class BeginIdempotentRequestUseCase(IIdempotencyStore store, IClock clock)
    : IUseCase<BeginIdempotentRequestCommand, BeginIdempotentRequestResultDto>
{
    public async Task<Result<BeginIdempotentRequestResultDto>> ExecuteAsync(
        BeginIdempotentRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new BeginIdempotentRequestCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<BeginIdempotentRequestResultDto>();
        }

        var request = command.Request;
        var candidate = new IdempotencyReservation(request, Guid.NewGuid());
        var now = clock.UtcNow;
        var result = await store.ReserveOrGetAsync(candidate, now, cancellationToken);
        if (result.IsFailed)
        {
            return Result.Fail<BeginIdempotentRequestResultDto>(result.Errors);
        }

        var stored = result.Value;
        var storedRequest = stored.Reservation.Request;
        if (storedRequest.Scope != request.Scope || storedRequest.Key != request.Key)
        {
            throw new InvalidOperationException("The idempotency store returned a record for another request scope or key.");
        }

        if (storedRequest.Fingerprint != request.Fingerprint || storedRequest.OperationId != request.OperationId)
        {
            return Result.Fail<BeginIdempotentRequestResultDto>(IdempotencyError.KeyReused());
        }

        return stored switch
        {
            IdempotencyRecord.InProgress when stored.Reservation.AttemptId == candidate.AttemptId =>
                Result.Ok<BeginIdempotentRequestResultDto>(new BeginIdempotentRequestResultDto.Started(stored.Reservation)),
            IdempotencyRecord.InProgress =>
                Result.Fail<BeginIdempotentRequestResultDto>(IdempotencyError.RequestInProgress()),
            IdempotencyRecord.Completed completed when completed.ExpiresAt > now =>
                Result.Ok<BeginIdempotentRequestResultDto>(new BeginIdempotentRequestResultDto.Completed(completed)),
            _ => throw new InvalidOperationException("The idempotency store returned an invalid record state."),
        };
    }
}
