using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Observability;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
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
using CloudSharp.TestSupport.Fakes;
using FluentResults;
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

    [Test]
    public async Task AddMembershipServices_ShouldExecuteListRolesWithRegisteredPersistence()
    {
        var now = new DateTimeOffset(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);
        var space = Space.Reconstitute(
            7, Guid.CreateVersion7(), 21, "Product", null, SpaceStatus.Active,
            null, 0, 0, 1, now, now, null, null, null);
        var role = SpaceRole.Reconstitute(
            101, Guid.CreateVersion7(), space.Id, "Admin", "ADMIN", SpaceRoleSystemKey.Admin,
            null, 2, true, 1, now, now, null, [SpacePermission.SpaceView]);
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var reader = new RoleReader(role);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISpaceRepository>(_ => repository);
        services.AddScoped<ISpaceRoleReader>(_ => reader);
        services.AddMembershipServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var useCase = scope.ServiceProvider
            .GetRequiredService<IUseCase<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>>>();
        var actor = CurrentActor.ForSession(
            space.OwnerUserId, Guid.CreateVersion7(), SystemRole.User, Guid.CreateVersion7());
        var authorization = new SpaceAuthorizationQuery(
            actor, space.PublicId, [SpacePermission.MembersView], [SpaceStatus.Active]);
        var context = SessionSpaceAuthorizationPolicy.Evaluate(
            authorization, new SpaceAuthorizationSnapshot(space, null, [])).Value;

        var result = await useCase.ExecuteAsync(new ListSpaceRolesQuery(context));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(useCase,
                Is.TypeOf<LoggingUseCaseDecorator<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>>>());
            Assert.That(repository.SearchedIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(reader.RequestedSpaceIds, Is.EqualTo(new[] { space.Id }));
            var returned = result.Value.Single();
            Assert.That(returned.RolePublicId, Is.EqualTo(role.PublicId));
            Assert.That(returned.Code, Is.EqualTo(SpaceRoleSystemKey.Admin));
            Assert.That(returned.IsManaged, Is.True);
        });
    }

    [Test]
    public async Task AddMembershipServices_ShouldExecuteListMembershipsWithRegisteredPersistence()
    {
        var now = new DateTimeOffset(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);
        var space = Space.Reconstitute(
            7, Guid.CreateVersion7(), 21, "Product", null, SpaceStatus.Active,
            null, 0, 0, 1, now, now, null, null, null);
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var reader = new MembershipReader();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISpaceRepository>(_ => repository);
        services.AddScoped<ISpaceMembershipReader>(_ => reader);
        services.AddMembershipServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var useCase = scope.ServiceProvider
            .GetRequiredService<IUseCase<ListSpaceMembershipsQuery, SpaceMembershipPageDto>>();
        var actor = CurrentActor.ForSession(
            space.OwnerUserId, Guid.CreateVersion7(), SystemRole.User, Guid.CreateVersion7());
        var authorization = new SpaceAuthorizationQuery(
            actor, space.PublicId, [SpacePermission.MembersView], [SpaceStatus.Active]);
        var context = SessionSpaceAuthorizationPolicy.Evaluate(
            authorization, new SpaceAuthorizationSnapshot(space, null, [])).Value;

        var result = await useCase.ExecuteAsync(new ListSpaceMembershipsQuery(context));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(useCase, Is.TypeOf<LoggingUseCaseDecorator<ListSpaceMembershipsQuery, SpaceMembershipPageDto>>());
            Assert.That(repository.SearchedIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(reader.RequestedScopes, Is.EqualTo(new[] { (space.Id, space.OwnerUserId, actor.UserId) }));
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.NextCursor, Is.Null);
        });
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

    private sealed class RoleReader(SpaceRole role) : ISpaceRoleReader
    {
        public List<long> RequestedSpaceIds { get; } = [];

        public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
            long spaceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedSpaceIds.Add(spaceId);
            IReadOnlyList<SpaceRole> roles = role.SpaceId == spaceId ? [role] : [];
            return Task.FromResult(Result.Ok(roles));
        }
    }

    private sealed class MembershipReader : ISpaceMembershipReader
    {
        public List<(long SpaceId, long OwnerUserId, long RequesterUserId)> RequestedScopes { get; } = [];

        public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
            long spaceId,
            long ownerUserId,
            long requesterUserId,
            string? cursor,
            int limit,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedScopes.Add((spaceId, ownerUserId, requesterUserId));
            return Task.FromResult(Result.Ok(new SpaceMembershipReadPage([], null)));
        }

        public Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
            long spaceId,
            Guid membershipPublicId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
