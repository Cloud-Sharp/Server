using System.Net;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ErrorHttpStatusCodeCatalog
{
    private static readonly IReadOnlyDictionary<string, HttpStatusCode> StatusCodes =
        new Dictionary<string, HttpStatusCode>(StringComparer.Ordinal)
        {
            [ResultHttpMapper.RequestValidationFailedCode] = HttpStatusCode.BadRequest,
            ["AUTH_TOKEN_REQUIRED"] = HttpStatusCode.Unauthorized,
            ["AUTH_TOKEN_INVALID"] = HttpStatusCode.Unauthorized,
            ["AUTH_TOKEN_TYPE_NOT_ALLOWED"] = HttpStatusCode.Unauthorized,
            ["PERMISSION_DENIED"] = HttpStatusCode.Forbidden,
            ["RESOURCE_NOT_FOUND"] = HttpStatusCode.NotFound,
            ["METHOD_NOT_ALLOWED"] = HttpStatusCode.MethodNotAllowed,
            ["REQUEST_CONFLICT"] = HttpStatusCode.Conflict,
            ["IDEMPOTENCY_KEY_REUSED"] = HttpStatusCode.Conflict,
            ["SECRET_RESPONSE_NOT_REPLAYABLE"] = HttpStatusCode.Conflict,
            ["PRECONDITION_FAILED"] = HttpStatusCode.PreconditionFailed,
            ["PRECONDITION_REQUIRED"] = (HttpStatusCode)428,
            ["RATE_LIMITED"] = HttpStatusCode.TooManyRequests,
            ["DEPENDENCY_UNAVAILABLE"] = HttpStatusCode.ServiceUnavailable,
        };

    public static bool TryGetStatusCode(string errorCode, out HttpStatusCode statusCode)
        => StatusCodes.TryGetValue(errorCode, out statusCode);
}
