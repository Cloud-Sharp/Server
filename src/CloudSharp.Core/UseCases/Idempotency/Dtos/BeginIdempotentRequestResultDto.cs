using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Core.UseCases.Idempotency.Dtos;

public abstract record BeginIdempotentRequestResultDto
{
    private BeginIdempotentRequestResultDto() { }

    public sealed record Started(IdempotencyReservation Reservation) : BeginIdempotentRequestResultDto;

    public sealed record Completed(IdempotencyRecord.Completed Record) : BeginIdempotentRequestResultDto;
}
