using CloudSharp.Core.Common.Time;
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
        services.AddAuthServices(configuration);
        services.AddPostgreSqlPersistence(configuration);
        services.AddIdempotencyServices();
        services.AddUserServices();
        services.AddSpaceServices();
        services.AddMountServices();
        services.AddMembershipServices();
        services.AddInviteServices();

        return services;
    }
}
