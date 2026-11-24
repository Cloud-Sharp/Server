using System.Diagnostics;
using CloudSharp.Api.Contracts.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Middleware;

internal sealed class InviteCapabilityProtectionMiddleware(RequestDelegate next)
{
    private const string PublicPrefix = "/public/v2/invites";
    private const string AcceptPrefix = "/api/v2/invites";
    internal const string PublicRoute = PublicPrefix + "/{inviteToken}";
    internal const string AcceptRoute = AcceptPrefix + "/{inviteToken}/accept";

    public Task InvokeAsync(HttpContext context, ILogger<InviteCapabilityProtectionMiddleware> logger)
    {
        var routeTemplate = FindRouteTemplate(context.Request.Path);
        if (routeTemplate is null)
        {
            return next(context);
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var activity = context.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        if (activity is not null)
        {
            activity.DisplayName = routeTemplate;
            activity.SetTag("http.route", routeTemplate);
            activity.SetTag("url.path", routeTemplate);
            activity.SetTag("url.query", null);
            activity.SetTag("url.full", null);
            activity.SetTag("http.target", null);
            activity.SetTag("http.url", null);
        }

        context.Response.OnCompleted(() =>
        {
            logger.LogInformation("Invite capability route {RouteTemplate} returned {StatusCode}.",
                routeTemplate, context.Response.StatusCode);
            return Task.CompletedTask;
        });

        return context.Request.IsHttps
            ? next(context)
            : ErrorHttpResults.ResourceNotFound().ExecuteAsync(context);
    }

    internal static string? FindRouteTemplate(PathString path)
    {
        // Cover unmatched methods and paths before HTTPS redirection can echo the capability.
        if (path.StartsWithSegments(PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PublicRoute;
        }

        return path.StartsWithSegments(AcceptPrefix, StringComparison.OrdinalIgnoreCase) ? AcceptRoute : null;
    }
}
