using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Idempotency;

internal sealed class IdempotencyRequestValidator : AbstractValidator<IdempotencyRequestIdentity>
{
    public IdempotencyRequestValidator()
    {
        RuleFor(request => request.Scope)
            .Must(scope => scope is not null
                && !string.IsNullOrWhiteSpace(scope.Actor)
                && !string.IsNullOrWhiteSpace(scope.Method)
                && !string.IsNullOrWhiteSpace(scope.CanonicalPath))
            .WithErrorCode(ErrorCodes.Idempotency.ScopeInvalid)
            .WithMessage("A normalized idempotency scope is required.");

        RuleFor(request => request.Key)
            .Must(key => key is { Length: >= 16 and <= 128 }
                && key.All(character => character is >= ' ' and <= '~'))
            .WithErrorCode(ErrorCodes.Idempotency.KeyInvalid)
            .WithMessage("The idempotency key must contain 16 to 128 printable ASCII characters.");

        RuleFor(request => request.Fingerprint)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.Idempotency.FingerprintInvalid)
            .WithMessage("A request fingerprint is required.");

        RuleFor(request => request.OperationId)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.Idempotency.OperationIdInvalid)
            .WithMessage("An operation identifier is required.");
    }
}
