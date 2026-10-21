using CloudSharp.Api.Contracts.Mapping;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Middleware;

internal static class UnsupportedMediaTypeStatusCodeHandler
{
    internal static async Task HandleAsync(StatusCodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var httpContext = context.HttpContext;
        if (httpContext.Response.StatusCode != StatusCodes.Status415UnsupportedMediaType)
        {
            return;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        await ErrorResponseWriter.WriteAsync(
            httpContext,
            StatusCodes.Status415UnsupportedMediaType,
            ResultHttpMapper.RequestValidationFailedCode,
            "One or more validation errors occurred.",
            []);
    }
}
