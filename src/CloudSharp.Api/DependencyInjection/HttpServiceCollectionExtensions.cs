using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Api.Contracts.ProblemDetails;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudSharp.Api.DependencyInjection;

/// <summary>Registers the HTTP contract shared by the host and endpoint delegate tests.</summary>
public static class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddCloudSharpHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidation();
        services.AddProblemDetails();
        services.Replace(ServiceDescriptor.Singleton<IProblemDetailsService, CloudSharpProblemDetailsService>());
        // Binding failures otherwise return an empty status response in Production.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        });

        return services;
    }
}
