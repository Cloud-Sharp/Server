namespace CloudSharp.Api.Middleware;

/// <summary>Applies cache protection before authentication, validation and body binding.</summary>
internal sealed class NoStoreResponseMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<NoStoreResponseMetadata>() is not null)
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
