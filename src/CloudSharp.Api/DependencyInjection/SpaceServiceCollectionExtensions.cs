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
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class SpaceServiceCollectionExtensions
{
    internal static void AddSpaceServices(this IServiceCollection services)
    {
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
    }
}
