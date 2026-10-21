using System.Net;
using CloudSharp.Api.Contracts.Mapping;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Contracts.ProblemDetails;

internal sealed class CloudSharpProblemDetailsService : IProblemDetailsService
{
    private const string ValidationMessage = "One or more validation errors occurred.";

    public async ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) =>
        await WriteCoreAsync(context);

    public async ValueTask WriteAsync(ProblemDetailsContext context) =>
        _ = await WriteCoreAsync(context);

    private static async Task<bool> WriteCoreAsync(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var errors = (context.ProblemDetails as HttpValidationProblemDetails)?.Errors
            ?? new Dictionary<string, string[]>();

        var statusCode = context.ProblemDetails.Status
            ?? httpContext.Response.StatusCode;
        if (statusCode < (int)HttpStatusCode.BadRequest)
        {
            statusCode = (int)HttpStatusCode.BadRequest;
        }

        IReadOnlyList<ErrorDetail> details = errors.Count == 0
            ? []
            : errors
                .Select(entry => new ErrorDetail(
                    ResultHttpMapper.ToCamelCase(entry.Key),
                    ErrorHttpResults.InvalidDetailCode))
                .ToArray();

        var isValidation = details.Count > 0 || statusCode == (int)HttpStatusCode.BadRequest;
        var code = isValidation
            ? ResultHttpMapper.RequestValidationFailedCode
            : ErrorResponseWriter.CodeForStatus(statusCode);
        var message = isValidation
            ? ValidationMessage
            : ErrorResponseWriter.MessageForCode(code);

        await ErrorResponseWriter.WriteAsync(httpContext, statusCode, code, message, details);
        return true;
    }
}
