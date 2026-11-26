using CloudSharp.Core.UseCases.Mounts.Archives;
using CloudSharp.Core.UseCases.Mounts.Creations;
using CloudSharp.Core.UseCases.Mounts.Details;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.Listings;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using CloudSharp.Core.UseCases.Mounts.PermissionListings;
using CloudSharp.Core.UseCases.Mounts.PermissionPreviews;
using CloudSharp.Core.UseCases.Mounts.Reactivations;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.DependencyInjection;

internal static class MountServiceCollectionExtensions
{
    internal static void AddMountServices(this IServiceCollection services)
    {
        services.AddLoggedUseCase<ListMountsQuery, MountPageDto, ListMountsUseCase>();
        services.AddLoggedUseCase<CreateMountCommand, MountResultDto, CreateMountUseCase>();
        services.AddLoggedUseCase<GetMountQuery, MountResultDto, GetMountUseCase>();
        services.AddLoggedUseCase<UpdateMountCommand, MountResultDto, UpdateMountUseCase>();
        services.AddLoggedUseCase<ArchiveMountCommand, MountResultDto, ArchiveMountUseCase>();
        services.AddLoggedUseCase<ReactivateMountCommand, MountResultDto, ReactivateMountUseCase>();
        services.AddLoggedUseCase<ListMountPermissionsQuery, MountPermissionPageDto, ListMountPermissionsUseCase>();
        services.AddLoggedUseCase<ReplaceMountRolePermissionsCommand, MountPermissionResultDto, ReplaceMountRolePermissionsUseCase>();
        services.AddLoggedUseCase<ReplaceMountUserPermissionsCommand, MountPermissionResultDto, ReplaceMountUserPermissionsUseCase>();
        services.AddLoggedUseCase<PreviewMountPermissionsQuery, MountPermissionPreviewPageDto, PreviewMountPermissionsUseCase>();
    }
}
