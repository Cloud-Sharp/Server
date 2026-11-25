namespace CloudSharp.Core.Common.Errors;

public sealed class IdempotencyError : CloudSharpError
{
    private IdempotencyError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static IdempotencyError KeyReused() =>
        new(ErrorCodes.Idempotency.KeyReused, "The idempotency key is associated with a different request.");

    public static IdempotencyError RequestInProgress() =>
        new(ErrorCodes.Idempotency.RequestInProgress, "A request with the same idempotency key is in progress.");
}
