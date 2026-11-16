using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.UseCases.Auth.Authentication;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Auth.Registrations;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Passwords;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class AuthServiceCollectionExtensions
{
    internal static void AddAuthServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTokenHashing(configuration);
        services.AddRedisSessionStore(configuration);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<SessionIssuanceService>();
        services.AddLoggedUseCase<RegistrationCommand, RegistrationResultDto, RegistrationUseCase>();
        services.AddLoggedUseCase<LoginCommand, LoginResultDto, LoginUseCase>();
        services.AddLoggedUseCase<PasswordChangeCommand, PasswordChangeResultDto, PasswordChangeUseCase>();
        services.AddScoped<AuthenticateSessionUseCase>();

        services.AddCloudSharpAuthentication();
    }
}
