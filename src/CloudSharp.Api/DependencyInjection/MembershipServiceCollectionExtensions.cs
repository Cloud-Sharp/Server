using CloudSharp.Core.UseCases.Memberships.Activations;
using CloudSharp.Core.UseCases.Memberships.Details;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Leaves;
using CloudSharp.Core.UseCases.Memberships.Listings;
using CloudSharp.Core.UseCases.Memberships.Removals;
using CloudSharp.Core.UseCases.Memberships.RoleListings;
using CloudSharp.Core.UseCases.Memberships.RoleReplacements;
using CloudSharp.Core.UseCases.Memberships.Suspensions;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class MembershipServiceCollectionExtensions
{
    internal static void AddMembershipServices(this IServiceCollection services)
    {
        services.AddLoggedUseCase<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>, ListSpaceRolesUseCase>();
        services.AddLoggedUseCase<ListSpaceMembershipsQuery, SpaceMembershipPageDto, ListSpaceMembershipsUseCase>();
        services.AddLoggedUseCase<GetSpaceMembershipQuery, SpaceMembershipResultDto, GetSpaceMembershipUseCase>();
        services.AddLoggedUseCase<ReplaceSpaceMemberRolesCommand, SpaceMembershipResultDto, ReplaceSpaceMemberRolesUseCase>();
        services.AddLoggedUseCase<SuspendSpaceMemberCommand, SpaceMembershipResultDto, SuspendSpaceMemberUseCase>();
        services.AddLoggedUseCase<ActivateSpaceMemberCommand, SpaceMembershipResultDto, ActivateSpaceMemberUseCase>();
        services.AddLoggedUseCase<RemoveSpaceMemberCommand, RemoveSpaceMemberResultDto, RemoveSpaceMemberUseCase>();
        services.AddLoggedUseCase<LeaveSpaceCommand, LeaveSpaceResultDto, LeaveSpaceUseCase>();
    }
}
