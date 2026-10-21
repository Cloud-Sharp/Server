using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Auth.Authentication;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Auth.Registrations;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Core.UseCases.Users.Profiles;
using CloudSharp.Infrastructure.Auth.Passwords;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCloudSharpApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock>(new SystemClock(TimeProvider.System));
        services.AddTokenHashing(configuration);
        services.AddRedisSessionStore(configuration);
        services.AddPostgreSqlPersistence(configuration);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<SessionIssuanceService>();
        services.AddScoped<RegistrationUseCase>();
        services.AddScoped<LoginUseCase>();
        services.AddScoped<PasswordChangeUseCase>();
        services.AddScoped<GetProfileUseCase>();
        services.AddScoped<AuthenticateSessionUseCase>();

        services.AddCloudSharpAuthentication();

        return services;
    }
}
