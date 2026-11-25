using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Api.Endpoints.Idempotency;

internal sealed record IdempotencyEndpointPolicy(
    string OperationId,
    int ResultVersion,
    Func<EndpointFilterInvocationContext, IdempotencyScope> GetScope,
    Func<EndpointFilterInvocationContext, object?> GetFingerprintInput,
    Func<HttpContext, IdempotencyRecord.Completed, IResult> Replay)
{
    internal const string HeaderName = "Idempotency-Key";
    internal const int MinimumKeyLength = 16;
    internal const int MaximumKeyLength = 128;
}
