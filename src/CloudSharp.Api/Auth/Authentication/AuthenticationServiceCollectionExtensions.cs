using CloudSharp.Api.Auth.CurrentUser;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.Auth.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCloudSharpAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICurrentActorAccessor, CurrentActorAccessor>();
        services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                SessionAuthenticationDefaults.UserSessionOnlyPolicy,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(
                        SessionClaimTypes.ActorType,
                        SessionAuthenticationDefaults.SessionActorType));
        });

        return services;
    }
}
