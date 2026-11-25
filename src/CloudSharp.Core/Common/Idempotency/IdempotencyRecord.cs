namespace CloudSharp.Core.Common.Idempotency;

public abstract record IdempotencyRecord(IdempotencyReservation Reservation)
{
    public sealed record InProgress(IdempotencyReservation Reservation, DateTimeOffset LeaseExpiresAt)
        : IdempotencyRecord(Reservation);

    public sealed record Completed(
        IdempotencyReservation Reservation,
        int ResultVersion,
        IdempotencyRecovery Recovery,
        DateTimeOffset CompletedAt) : IdempotencyRecord(Reservation)
    {
        public DateTimeOffset ExpiresAt => CompletedAt.AddHours(24);
    }
}
