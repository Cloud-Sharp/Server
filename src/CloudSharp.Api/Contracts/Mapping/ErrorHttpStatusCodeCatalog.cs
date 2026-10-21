using System.Net;
using CloudSharp.Core.Common.Errors;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ErrorHttpStatusCodeCatalog
{
    private static readonly IReadOnlyDictionary<string, HttpStatusCode> StatusCodes =
        new Dictionary<string, HttpStatusCode>(StringComparer.Ordinal)
        {
            [ResultHttpMapper.RequestValidationFailedCode] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.InvalidEmail] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.NameInvalid] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.DisplayNameInvalid] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.PasswordInvalid] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.PasswordMismatch] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.InvalidRole] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.InvalidStatus] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.Deleted] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.InvalidState] = HttpStatusCode.BadRequest,
            [ErrorCodes.User.EmailConflict] = HttpStatusCode.Conflict,
            [ErrorCodes.User.NameConflict] = HttpStatusCode.Conflict,
            [ErrorCodes.Session.AuthInvalidCredentials] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Session.AuthSessionInvalid] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Session.AuthSessionExpired] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Session.AuthUserInactive] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Auth.TokenRequired] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Auth.TokenInvalid] = HttpStatusCode.Unauthorized,
            [ErrorCodes.Auth.TokenTypeNotAllowed] = HttpStatusCode.Unauthorized,
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
