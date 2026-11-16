using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Observability;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Memberships.Activations;
using CloudSharp.Core.UseCases.Memberships.Details;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Leaves;
using CloudSharp.Core.UseCases.Memberships.Listings;
using CloudSharp.Core.UseCases.Memberships.Removals;
using CloudSharp.Core.UseCases.Memberships.RoleListings;
using CloudSharp.Core.UseCases.Memberships.RoleReplacements;
using CloudSharp.Core.UseCases.Memberships.Suspensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.DependencyInjection;

[TestFixture]
public class MembershipServiceCollectionExtensionsTests
{
    [Test]
    public void AddCloudSharpApplication_ShouldRegisterScopedLoggedMembershipUseCases()
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

        AssertRegistration<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>, ListSpaceRolesUseCase>(firstScope, secondScope);
        AssertRegistration<ListSpaceMembershipsQuery, SpaceMembershipPageDto, ListSpaceMembershipsUseCase>(firstScope, secondScope);
        AssertRegistration<GetSpaceMembershipQuery, SpaceMembershipResultDto, GetSpaceMembershipUseCase>(firstScope, secondScope);
        AssertRegistration<ReplaceSpaceMemberRolesCommand, SpaceMembershipResultDto, ReplaceSpaceMemberRolesUseCase>(firstScope, secondScope);
        AssertRegistration<SuspendSpaceMemberCommand, SpaceMembershipResultDto, SuspendSpaceMemberUseCase>(firstScope, secondScope);
        AssertRegistration<ActivateSpaceMemberCommand, SpaceMembershipResultDto, ActivateSpaceMemberUseCase>(firstScope, secondScope);
        AssertRegistration<RemoveSpaceMemberCommand, RemoveSpaceMemberResultDto, RemoveSpaceMemberUseCase>(firstScope, secondScope);
        AssertRegistration<LeaveSpaceCommand, LeaveSpaceResultDto, LeaveSpaceUseCase>(firstScope, secondScope);
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
