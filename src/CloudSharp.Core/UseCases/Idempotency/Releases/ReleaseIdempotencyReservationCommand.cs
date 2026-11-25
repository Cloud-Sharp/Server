using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Idempotency.Releases;

public sealed record ReleaseIdempotencyReservationCommand(IdempotencyReservation Reservation)
{
    public override string ToString() => "ReleaseIdempotencyReservationCommand { Reservation = [redacted] }";
}

internal sealed class ReleaseIdempotencyReservationCommandValidator : AbstractValidator<ReleaseIdempotencyReservationCommand>
{
    public ReleaseIdempotencyReservationCommandValidator()
    {
        RuleFor(command => command.Reservation)
            .NotNull()
            .WithErrorCode(ErrorCodes.Idempotency.AttemptIdInvalid)
            .WithMessage("An idempotency reservation is required.");

        When(command => command.Reservation is not null, () =>
        {
            RuleFor(command => command.Reservation.AttemptId)
                .NotEmpty()
                .WithErrorCode(ErrorCodes.Idempotency.AttemptIdInvalid)
                .WithMessage("An execution attempt identifier is required.");

            RuleFor(command => command.Reservation.Request)
                .NotNull()
                .WithErrorCode(ErrorCodes.Idempotency.ScopeInvalid)
                .WithMessage("An idempotency request is required.")
                .SetValidator(new IdempotencyRequestValidator());
        });
    }
}
