using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Api.Observability;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Auth.Authentication;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Auth.Registrations;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Core.UseCases.Spaces.Archives;
using CloudSharp.Core.UseCases.Spaces.Creations;
using CloudSharp.Core.UseCases.Spaces.DeletionRequests;
using CloudSharp.Core.UseCases.Spaces.Details;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.Listings;
using CloudSharp.Core.UseCases.Spaces.MetadataUpdates;
using CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;
using CloudSharp.Core.UseCases.Spaces.QuotaChanges;
using CloudSharp.Core.UseCases.Spaces.QuotaReads;
using CloudSharp.Core.UseCases.Spaces.Reactivations;
using CloudSharp.Core.UseCases.Users.Profiles;
using CloudSharp.Infrastructure.Auth.Passwords;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddLoggedUseCase<RegistrationCommand, RegistrationResultDto, RegistrationUseCase>();
        services.AddLoggedUseCase<LoginCommand, LoginResultDto, LoginUseCase>();
        services.AddLoggedUseCase<PasswordChangeCommand, PasswordChangeResultDto, PasswordChangeUseCase>();
        services.AddScoped<GetProfileUseCase>();
        services.AddScoped<AuthenticateSessionUseCase>();

        services.AddLoggedUseCase<ListSpacesQuery, SpacePageDto, ListSpacesUseCase>();
        services.AddLoggedUseCase<CreateSpaceCommand, SpaceResultDto, CreateSpaceUseCase>();
        services.AddLoggedUseCase<GetSpaceQuery, SpaceResultDto, GetSpaceUseCase>();
        services.AddLoggedUseCase<UpdateSpaceCommand, SpaceResultDto, UpdateSpaceUseCase>();
        services.AddLoggedUseCase<ArchiveSpaceCommand, SpaceResultDto, ArchiveSpaceUseCase>();
        services.AddLoggedUseCase<ReactivateSpaceCommand, SpaceResultDto, ReactivateSpaceUseCase>();
        services.AddLoggedUseCase<RequestSpaceDeletionCommand, SpaceDeletionAcceptedDto, RequestSpaceDeletionUseCase>();
        services.AddLoggedUseCase<GetSpaceQuotaQuery, SpaceQuotaDto, GetSpaceQuotaUseCase>();
        services.AddLoggedUseCase<ChangeSpaceQuotaCommand, SpaceQuotaDto, ChangeSpaceQuotaUseCase>();
        services.AddLoggedUseCase<TransferSpaceOwnershipCommand, SpaceResultDto, TransferSpaceOwnershipUseCase>();

        services.AddCloudSharpAuthentication();

        return services;
    }

    private static void AddLoggedUseCase<TInput, TOutput, TUseCase>(this IServiceCollection services)
        where TUseCase : class, IUseCase<TInput, TOutput>
    {
        services.AddScoped<TUseCase>();
        services.AddScoped<IUseCase<TInput, TOutput>>(provider =>
            new LoggingUseCaseDecorator<TInput, TOutput>(
                provider.GetRequiredService<TUseCase>(),
                provider.GetRequiredService<ILogger<LoggingUseCaseDecorator<TInput, TOutput>>>()));
    }
}
