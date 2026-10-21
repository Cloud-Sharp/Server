using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Middleware;

internal sealed class ErrorHandlingMiddleware
{
    private const string ValidationMessage = "One or more validation errors occurred.";

    private readonly RequestDelegate _next;

    public ErrorHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<ErrorHandlingMiddleware> logger)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            await WriteValidationFailureAsync(context, exception.StatusCode);
        }
        catch (JsonException) when (!context.Response.HasStarted)
        {
            await WriteValidationFailureAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(
                "Unhandled request exception. ExceptionType={ExceptionType}, TraceId={TraceId}",
                exception.GetType().FullName,
                context.TraceIdentifier);

            await ErrorResponseWriter.WriteAsync(
                context,
                (int)HttpStatusCode.InternalServerError,
                ResultHttpMapper.InternalServerErrorCode,
                "An unexpected error occurred.",
                []);
        }
    }

    private static Task WriteValidationFailureAsync(
        HttpContext context,
        int statusCode = (int)HttpStatusCode.BadRequest) =>
        ErrorResponseWriter.WriteAsync(
            context,
            statusCode,
            ResultHttpMapper.RequestValidationFailedCode,
            ValidationMessage,
            []);
}
