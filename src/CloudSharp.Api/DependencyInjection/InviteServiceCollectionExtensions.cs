using CloudSharp.Core.UseCases.Invites.Acceptances;
using CloudSharp.Core.UseCases.Invites.Creations;
using CloudSharp.Core.UseCases.Invites.Details;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.Listings;
using CloudSharp.Core.UseCases.Invites.PublicInfo;
using CloudSharp.Core.UseCases.Invites.Revocations;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class InviteServiceCollectionExtensions
{
    internal static void AddInviteServices(this IServiceCollection services)
    {
        services.AddLoggedUseCase<ListSpaceInvitesQuery, SpaceInvitePageDto, ListSpaceInvitesUseCase>();
        services.AddLoggedUseCase<CreateSpaceInviteCommand, CreateSpaceInviteResultDto, CreateSpaceInviteUseCase>();
        services.AddLoggedUseCase<RevokeSpaceInviteCommand, RevokeSpaceInviteResultDto, RevokeSpaceInviteUseCase>();
        services.AddLoggedUseCase<GetPublicSpaceInviteQuery, PublicSpaceInviteDto, GetPublicSpaceInviteUseCase>();
        services.AddLoggedUseCase<AcceptSpaceInviteCommand, AcceptSpaceInviteResultDto, AcceptSpaceInviteUseCase>();
        services.AddLoggedUseCase<GetSpaceInviteQuery, SpaceInviteResultDto, GetSpaceInviteUseCase>();
    }
}
