using CloudSharp.Api.Observability;
using CloudSharp.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.DependencyInjection;

internal static class UseCaseServiceCollectionExtensions
{
    internal static void AddLoggedUseCase<TInput, TOutput, TUseCase>(this IServiceCollection services)
        where TUseCase : class, IUseCase<TInput, TOutput>
    {
        services.AddScoped<TUseCase>();
        services.AddScoped<IUseCase<TInput, TOutput>>(provider =>
            new LoggingUseCaseDecorator<TInput, TOutput>(
                provider.GetRequiredService<TUseCase>(),
                provider.GetRequiredService<ILogger<LoggingUseCaseDecorator<TInput, TOutput>>>()));
    }
}
