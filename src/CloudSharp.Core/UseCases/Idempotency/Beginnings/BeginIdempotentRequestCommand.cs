using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Idempotency.Beginnings;

public sealed record BeginIdempotentRequestCommand(IdempotencyRequestIdentity Request)
{
    public override string ToString() => "BeginIdempotentRequestCommand { Request = [redacted] }";
}

internal sealed class BeginIdempotentRequestCommandValidator : AbstractValidator<BeginIdempotentRequestCommand>
{
    public BeginIdempotentRequestCommandValidator()
    {
        RuleFor(command => command.Request)
            .NotNull()
            .WithErrorCode(ErrorCodes.Idempotency.ScopeInvalid)
            .WithMessage("An idempotency request is required.")
            .SetValidator(new IdempotencyRequestValidator());
    }
}
