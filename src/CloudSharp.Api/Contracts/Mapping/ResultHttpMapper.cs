using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Core.Common.Errors;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Contracts.Mapping;

public static class ResultHttpMapper
{
    public const string RequestValidationFailedCode = "REQUEST_VALIDATION_FAILED";
    public const string InternalServerErrorCode = "INTERNAL_SERVER_ERROR";
    private const string ValidationMessage = "One or more validation errors occurred.";
    private const string InternalMessage = "An unexpected error occurred.";

    private const string PropertyNameMetadataKey = "PropertyName";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return new FailureResult(result.Errors);
    }

    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return new FailureResult(result.Errors);
    }

    internal static (int StatusCode, ErrorResponse Body) BuildErrorResponse(IReadOnlyList<IError> errors, string requestId)
    {
        if (errors.Count == 0)
        {
            return Safe500(requestId);
        }

        var validationErrors = errors
            .Where(e => !e.Metadata.ContainsKey(CloudSharpError.StatusCodeMetadataKey)
                        && e.Metadata.ContainsKey(PropertyNameMetadataKey))
            .ToList();

        if (validationErrors.Count > 0)
        {
            var details = validationErrors
                .Select(e => new ErrorDetail(
                    Field: ToCamelCase(e.Metadata[PropertyNameMetadataKey] as string ?? string.Empty),
                    Code: e.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var ec)
                        ? ec as string ?? string.Empty
                        : string.Empty))
                .ToList();
            var body = new ErrorResponse(
                requestId,
                new ErrorBody(RequestValidationFailedCode, ValidationMessage, details));
            return ((int)HttpStatusCode.BadRequest, body);
        }

        var businessError = errors.FirstOrDefault(
            e => e.Metadata.ContainsKey(CloudSharpError.StatusCodeMetadataKey));
        if (businessError is null)
        {
            return Safe500(requestId);
        }

        if (businessError.Metadata[CloudSharpError.StatusCodeMetadataKey] is not HttpStatusCode status
            || !IsValidClientErrorStatus(status))
        {
            return Safe500(requestId);
        }

        if (businessError.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var codeObj)
            && codeObj is string code && !string.IsNullOrEmpty(code))
        {
            var body = new ErrorResponse(
                requestId,
                new ErrorBody(code, businessError.Message, []));
            return ((int)status, body);
        }

        return Safe500(requestId);
    }

    private static (int StatusCode, ErrorResponse Body) Safe500(string requestId)
    {
        var body = new ErrorResponse(
            requestId,
            new ErrorBody(InternalServerErrorCode, InternalMessage, []));
        return ((int)HttpStatusCode.InternalServerError, body);
    }

    private static bool IsValidClientErrorStatus(HttpStatusCode status)
        => (int)status is >= 400 and <= 599;

    internal static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var parts = propertyName.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var segment = parts[i];
            if (segment.Length == 0 || segment[0] == '[')
            {
                continue;
            }

            var chars = segment.ToCharArray();
            chars[0] = char.ToLowerInvariant(chars[0]);
            parts[i] = new string(chars);
        }

        return string.Join('.', parts);
    }
}

internal sealed class FailureResult : IResult
{
    private readonly IReadOnlyList<IError> _errors;

    public FailureResult(IReadOnlyList<IError> errors)
    {
        _errors = errors;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var (statusCode, body) = ResultHttpMapper.BuildErrorResponse(_errors, httpContext.TraceIdentifier);

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            LogFallback(httpContext);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsJsonAsync(body, ResultHttpMapper.JsonOptions, httpContext.RequestAborted);
    }

    private void LogFallback(HttpContext httpContext)
    {
        var loggerFactory = httpContext.RequestServices.GetService<ILoggerFactory>();
        if (loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger(nameof(ResultHttpMapper));
        var errorTypes = string.Join(",", _errors.Select(e => e.GetType().Name));
        logger.LogError(
            "ResultHttpMapper fallback to 500. ErrorCount={ErrorCount}, ErrorTypes={ErrorTypes}, TraceId={TraceId}",
            _errors.Count,
            errorTypes,
            httpContext.TraceIdentifier);
    }
}