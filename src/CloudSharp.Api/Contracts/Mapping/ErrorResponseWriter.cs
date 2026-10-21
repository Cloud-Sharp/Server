using System.Net;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Core.Common.Errors;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ErrorResponseWriter
{
    internal static async Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        IReadOnlyList<ErrorDetail> details)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var body = new ErrorResponse(
            httpContext.TraceIdentifier,
            new ErrorBody(code, message, details));

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsJsonAsync(
            body,
            ResultHttpMapper.JsonOptions,
            httpContext.RequestAborted);
    }

    internal static string CodeForStatus(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.Unauthorized => ErrorCodes.Auth.TokenInvalid,
        (int)HttpStatusCode.Forbidden => "PERMISSION_DENIED",
        (int)HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
        (int)HttpStatusCode.MethodNotAllowed => "METHOD_NOT_ALLOWED",
        (int)HttpStatusCode.Conflict => "REQUEST_CONFLICT",
        (int)HttpStatusCode.PreconditionFailed => ErrorCodes.Common.PreconditionFailed,
        (int)HttpStatusCode.TooManyRequests => "RATE_LIMITED",
        (int)HttpStatusCode.ServiceUnavailable => ErrorCodes.Common.DependencyUnavailable,
        (int)HttpStatusCode.InternalServerError => ResultHttpMapper.InternalServerErrorCode,
        _ => ResultHttpMapper.RequestValidationFailedCode,
    };

    internal static string MessageForCode(string code) => code switch
    {
        ErrorCodes.Auth.TokenRequired => "Authentication token is required.",
        ErrorCodes.Auth.TokenInvalid => "The authentication token is invalid.",
        ErrorCodes.Auth.TokenTypeNotAllowed => "The authentication token type is not allowed.",
        ErrorCodes.Common.DependencyUnavailable => "A required dependency is unavailable.",
        ErrorCodes.Common.PreconditionFailed => "Resource version does not match the request.",
        ErrorCodes.Common.PreconditionRequired => "An If-Match header is required.",
        ResultHttpMapper.InternalServerErrorCode => "An unexpected error occurred.",
        _ => "One or more validation errors occurred.",
    };
}

