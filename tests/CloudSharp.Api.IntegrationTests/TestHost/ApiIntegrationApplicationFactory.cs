using CloudSharp.Core.Common.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed class ApiIntegrationApplicationFactory(
    string postgresConnectionString,
    string redisConnectionString,
    string tokenHashingKey,
    IClock clock,
    Action<IServiceCollection, ApiIntegrationEnvironment>? configureTestServices,
    ApiIntegrationEnvironment environment) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:PostgreSql", postgresConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", redisConnectionString);
        builder.UseSetting("TokenHashing:ActiveKey", tokenHashingKey);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = postgresConnectionString,
                ["ConnectionStrings:Redis"] = redisConnectionString,
                ["TokenHashing:ActiveKey"] = tokenHashingKey,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton(clock);
            configureTestServices?.Invoke(services, environment);
        });
    }
}
