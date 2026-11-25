using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Results;
using FluentResults;

namespace CloudSharp.Core.UseCases.Idempotency.Releases;

public sealed class ReleaseIdempotencyReservationUseCase(IIdempotencyStore store)
    : IUseCase<ReleaseIdempotencyReservationCommand, bool>
{
    public Task<Result<bool>> ExecuteAsync(
        ReleaseIdempotencyReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReleaseIdempotencyReservationCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<bool>());
        }

        return store.ReleaseAsync(command.Reservation, cancellationToken);
    }
}
