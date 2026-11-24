using CloudSharp.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Observability;

internal static class InviteCapabilityLogging
{
    internal static void AddInviteCapabilityLogProtection(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddOptions<LoggerFilterOptions>()
            .PostConfigure<IHttpContextAccessor>(ProtectCapabilityPaths);
    }

    private static void ProtectCapabilityPaths(LoggerFilterOptions options, IHttpContextAccessor accessor)
    {
        // Preserve the configured provider/category selection and its default minimum level.
        options.Rules.Insert(0, new LoggerFilterRule(null, null, options.MinLevel, null));
        for (var index = 0; index < options.Rules.Count; index++)
        {
            var rule = options.Rules[index];
            options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, rule.LogLevel,
                (provider, category, level) => !ContainsCapabilityPath(category, accessor.HttpContext)
                    && (rule.Filter?.Invoke(provider, category, level) ?? true));
        }
    }

    private static bool ContainsCapabilityPath(string? category, HttpContext? context) =>
        context is not null
        && (category == "Microsoft.AspNetCore.Hosting.Diagnostics"
            || category?.StartsWith("Microsoft.AspNetCore.Routing.Matching.", StringComparison.Ordinal) == true)
        && InviteCapabilityProtectionMiddleware.FindRouteTemplate(context.Request.Path) is not null;
}
