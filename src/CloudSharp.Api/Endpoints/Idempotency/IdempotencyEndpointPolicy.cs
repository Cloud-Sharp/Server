using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Api.Endpoints.Idempotency;

internal sealed record IdempotencyEndpointPolicy(
    string OperationId,
    int ResultVersion,
    Func<EndpointFilterInvocationContext, IdempotencyScope> GetScope,
    Func<EndpointFilterInvocationContext, object?> GetFingerprintInput,
    Type TransactionResultType,
    Func<object?, IdempotencyRecovery> CreateRecovery,
    Func<HttpContext, IdempotencyRecord.Completed, IResult> Replay)
{
    internal const string HeaderName = "Idempotency-Key";
    internal const int MinimumKeyLength = 16;
    internal const int MaximumKeyLength = 128;

    internal static IdempotencyEndpointPolicy Create<T>(
        string operationId,
        int resultVersion,
        Func<EndpointFilterInvocationContext, IdempotencyScope> getScope,
        Func<EndpointFilterInvocationContext, object?> getFingerprintInput,
        Func<T, IdempotencyRecovery> createRecovery,
        Func<HttpContext, IdempotencyRecord.Completed, IResult> replay) =>
        new(operationId, resultVersion, getScope, getFingerprintInput,
            typeof(T), value => createRecovery((T)value!), replay);
}
