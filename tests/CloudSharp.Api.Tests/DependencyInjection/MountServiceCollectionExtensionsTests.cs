using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Observability;
using CloudSharp.Core.UseCases;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.DependencyInjection;

[TestFixture]
public class MountServiceCollectionExtensionsTests
{
    [Test]
    public void AddCloudSharpApplication_ShouldRegisterScopedLoggedMountUseCases()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TokenHashing:ActiveKey"] = Convert.ToBase64String(new byte[32]),
                ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=cloudsharp_test;Username=test;Password=test",
                ["ConnectionStrings:Redis"] = "localhost:6379",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCloudSharpApplication(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        AssertRegistration<ListMountsQuery, MountPageDto, ListMountsUseCase>(firstScope, secondScope);
        AssertRegistration<CreateMountCommand, MountResultDto, CreateMountUseCase>(firstScope, secondScope);
        AssertRegistration<GetMountQuery, MountResultDto, GetMountUseCase>(firstScope, secondScope);
        AssertRegistration<UpdateMountCommand, MountResultDto, UpdateMountUseCase>(firstScope, secondScope);
        AssertRegistration<ArchiveMountCommand, MountResultDto, ArchiveMountUseCase>(firstScope, secondScope);
        AssertRegistration<ReactivateMountCommand, MountResultDto, ReactivateMountUseCase>(firstScope, secondScope);
        AssertRegistration<ListMountPermissionsQuery, MountPermissionPageDto, ListMountPermissionsUseCase>(firstScope, secondScope);
        AssertRegistration<ReplaceMountRolePermissionsCommand, MountPermissionResultDto, ReplaceMountRolePermissionsUseCase>(firstScope, secondScope);
        AssertRegistration<ReplaceMountUserPermissionsCommand, MountPermissionResultDto, ReplaceMountUserPermissionsUseCase>(firstScope, secondScope);
        AssertRegistration<PreviewMountPermissionsQuery, MountPermissionPreviewPageDto, PreviewMountPermissionsUseCase>(firstScope, secondScope);
    }

    private static void AssertRegistration<TInput, TOutput, TUseCase>(IServiceScope firstScope, IServiceScope secondScope)
        where TUseCase : class, IUseCase<TInput, TOutput>
    {
        var firstProvider = firstScope.ServiceProvider;
        var secondProvider = secondScope.ServiceProvider;
        var decorated = firstProvider.GetRequiredService<IUseCase<TInput, TOutput>>();
        var concrete = firstProvider.GetRequiredService<TUseCase>();

        Assert.Multiple(() =>
        {
            Assert.That(firstProvider.GetServices<IUseCase<TInput, TOutput>>(), Is.EqualTo(new[] { decorated }));
            Assert.That(decorated, Is.TypeOf<LoggingUseCaseDecorator<TInput, TOutput>>());
            Assert.That(firstProvider.GetRequiredService<IUseCase<TInput, TOutput>>(), Is.SameAs(decorated));
            Assert.That(secondProvider.GetRequiredService<IUseCase<TInput, TOutput>>(), Is.Not.SameAs(decorated));
            Assert.That(firstProvider.GetRequiredService<TUseCase>(), Is.SameAs(concrete));
            Assert.That(secondProvider.GetRequiredService<TUseCase>(), Is.Not.SameAs(concrete));
        });
    }
}
