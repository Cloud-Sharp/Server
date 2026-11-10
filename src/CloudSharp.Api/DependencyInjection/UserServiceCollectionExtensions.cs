using CloudSharp.Core.UseCases.Users.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class UserServiceCollectionExtensions
{
    internal static void AddUserServices(this IServiceCollection services)
    {
        services.AddScoped<GetProfileUseCase>();
    }
}
