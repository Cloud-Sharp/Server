using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogScopeKey = "CorrelationId";
    public const int MaxLength = 128;

    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._:".ToCharArray();

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            [LogScopeKey] = correlationId,
        }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(IHeaderDictionary headers)
    {
        var values = headers[HeaderName];
        if (values.Count == 1
            && TryNormalize(values[0]!, out var normalized)
            && normalized.Length <= MaxLength)
        {
            return normalized;
        }

        return GenerateServerId();
    }

    private static bool TryNormalize(string raw, out string normalized)
    {
        normalized = raw.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var c in normalized)
        {
            if (Array.IndexOf(AllowedChars, c) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string GenerateServerId()
    {
        return Guid.CreateVersion7().ToString("N");
    }
}