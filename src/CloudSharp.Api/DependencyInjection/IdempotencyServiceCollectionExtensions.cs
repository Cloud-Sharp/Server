using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Idempotency.Beginnings;
using CloudSharp.Core.UseCases.Idempotency.Dtos;
using CloudSharp.Core.UseCases.Idempotency.Execution;
using CloudSharp.Core.UseCases.Idempotency.Releases;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class IdempotencyServiceCollectionExtensions
{
    internal static void AddIdempotencyServices(this IServiceCollection services)
    {
        services.AddScoped<IUseCase<BeginIdempotentRequestCommand, BeginIdempotentRequestResultDto>, BeginIdempotentRequestUseCase>();
        services.AddScoped<IUseCase<ReleaseIdempotencyReservationCommand, bool>, ReleaseIdempotencyReservationUseCase>();
        services.AddScoped<IdempotentRequestExecutor>();
    }
}
