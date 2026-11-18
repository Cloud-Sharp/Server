using CloudSharp.Api.DependencyInjection;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Spaces.Archives;
using CloudSharp.Core.UseCases.Spaces.Creations;
using CloudSharp.Core.UseCases.Spaces.Details;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.Listings;
using CloudSharp.Core.UseCases.Spaces.MetadataUpdates;
using CloudSharp.Core.UseCases.Spaces.Reactivations;
using CloudSharp.Infrastructure.Auth.Authorization;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.DependencyInjection;

[TestFixture]
public class SpaceServiceCollectionExtensionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 3, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AddCloudSharpApplication_ShouldResolveSpaceMutationsWithoutSpacePersistence()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TokenHashing:ActiveKey"] = Convert.ToBase64String(new byte[32]),
            ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=cloudsharp_test;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379",
        });
        builder.Services.AddCloudSharpApplication(builder.Configuration);
        builder.Services.AddScoped<ISpaceDetailsReader, UnavailableSpaceDetailsReader>();

        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var active = CreateSpaceSnapshot(SpaceStatus.Active);
        var archived = CreateSpaceSnapshot(SpaceStatus.Archived);
        var activeContext = AuthorizeOwner(active);
        var archivedContext = AuthorizeOwner(archived);
        var update = await serviceProvider.GetRequiredService<IUseCase<UpdateSpaceCommand, SpaceResultDto>>()
            .ExecuteAsync(new UpdateSpaceCommand(activeContext, active.Space.Version, "Updated", null));
        var archive = await serviceProvider.GetRequiredService<IUseCase<ArchiveSpaceCommand, SpaceResultDto>>()
            .ExecuteAsync(new ArchiveSpaceCommand(activeContext, active.Space.Version));
        var reactivate = await serviceProvider.GetRequiredService<IUseCase<ReactivateSpaceCommand, SpaceResultDto>>()
            .ExecuteAsync(new ReactivateSpaceCommand(archivedContext, archived.Space.Version));

        using (Assert.EnterMultipleScope())
        {
            foreach (var result in new[] { update, archive, reactivate })
            {
                Assert.That(result.IsFailed, Is.True);
                Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                    Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
            }
        }
    }

    [Test]
    public void AddCloudSharpApplication_ShouldResolveSpaceAuthorizationServiceAsScoped()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TokenHashing:ActiveKey"] = Convert.ToBase64String(new byte[32]),
            ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=cloudsharp_test;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379",
        });
        builder.Services.AddCloudSharpApplication(builder.Configuration);

        using var app = builder.Build();
        using var firstScope = app.Services.CreateScope();
        using var secondScope = app.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationService>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationService>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationService>();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<SpaceAuthorizationService>());
            Assert.That(sameScope, Is.SameAs(first));
            Assert.That(otherScope, Is.Not.SameAs(first));
        });
    }

    [Test]
    public async Task AddSpaceServices_ShouldUseRegisteredSpacePersistence()
    {
        var snapshot = CreateSpaceSnapshot(SpaceStatus.Active);
        var repository = new FakeSpaceRepository();
        repository.Seed(snapshot.Space);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISpaceDetailsReader>(_ => new FakeSpaceDetailsReader { Snapshot = snapshot });
        services.AddScoped<ISpaceRepository>(_ => repository);
        services.AddScoped<IUnitOfWork>(_ => new FakeSpaceUnitOfWork(repository));
        services.AddScoped<ITransactionExecutor, FakeTransactionExecutor>();
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddSpaceServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<UpdateSpaceCommand, SpaceResultDto>>();

        var result = await useCase.ExecuteAsync(
            new UpdateSpaceCommand(AuthorizeOwner(snapshot), snapshot.Space.Version, "Updated", null));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(repository.PersistedSpacesById[snapshot.Space.Id].Name, Is.EqualTo("Updated"));
    }

    [Test]
    public async Task AddSpaceServices_ShouldResolveScopedListGetAndCreateUseCases()
    {
        var snapshot = CreateSpaceSnapshot(SpaceStatus.Active);
        var actor = CurrentActor.ForSession(
            snapshot.Space.OwnerUserId, snapshot.OwnerUserPublicId, SystemRole.User, Guid.CreateVersion7());
        var detailsReader = new FakeSpaceDetailsReader { Snapshot = snapshot };
        var listReader = new FakeSpaceListCandidateReader(
        [
            new SpaceListCandidate(
                new SpaceAuthorizationSnapshot(snapshot.Space, null, []),
                snapshot.OwnerUserPublicId,
                "space-1"),
        ]);
        var userRepository = new FakeUserRepository { FoundByIdUser = CreateAccount(actor) };
        var spaceRepository = new FakeSpaceRepository();
        var bootstrapWriter = new FakeSpaceBootstrapWriter();
        var unitOfWork = new FakeSpaceUnitOfWork(spaceRepository, bootstrapWriter);
        var transactionExecutor = new FakeTransactionExecutor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISpaceListCandidateReader>(_ => listReader);
        services.AddScoped<ISpaceDetailsReader>(_ => detailsReader);
        services.AddScoped<IUserRepository>(_ => userRepository);
        services.AddScoped<ISpaceRepository>(_ => spaceRepository);
        services.AddScoped<ISpaceBootstrapWriter>(_ => bootstrapWriter);
        services.AddScoped<IUnitOfWork>(_ => unitOfWork);
        services.AddScoped<ITransactionExecutor>(_ => transactionExecutor);
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddSpaceServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var list = firstScope.ServiceProvider.GetRequiredService<IUseCase<ListSpacesQuery, SpacePageDto>>();
        var get = firstScope.ServiceProvider.GetRequiredService<IUseCase<GetSpaceQuery, SpaceResultDto>>();
        var create = firstScope.ServiceProvider.GetRequiredService<IUseCase<CreateSpaceCommand, SpaceResultDto>>();

        var listResult = await list.ExecuteAsync(new ListSpacesQuery(actor));
        var getResult = await get.ExecuteAsync(new GetSpaceQuery(AuthorizeOwner(snapshot)));
        var createResult = await create.ExecuteAsync(new CreateSpaceCommand(actor, "Created", null, 1024));

        Assert.Multiple(() =>
        {
            Assert.That(listResult.IsSuccess, Is.True, listResult.ToString());
            Assert.That(getResult.IsSuccess, Is.True, getResult.ToString());
            Assert.That(createResult.IsSuccess, Is.True, createResult.ToString());
            Assert.That(listResult.Value.Items.Select(item => item.SpacePublicId),
                Is.EqualTo(new[] { snapshot.Space.PublicId }));
            Assert.That(getResult.Value.Space.SpacePublicId, Is.EqualTo(snapshot.Space.PublicId));
            Assert.That(createResult.Value.Space.MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(bootstrapWriter.Saved.Single().Roles, Has.Count.EqualTo(3));
            Assert.That(unitOfWork.SaveChangesCallCount, Is.EqualTo(2));
            Assert.That(transactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(firstScope.ServiceProvider.GetRequiredService<IUseCase<ListSpacesQuery, SpacePageDto>>(),
                Is.SameAs(list));
            Assert.That(firstScope.ServiceProvider.GetRequiredService<IUseCase<GetSpaceQuery, SpaceResultDto>>(),
                Is.SameAs(get));
            Assert.That(firstScope.ServiceProvider.GetRequiredService<IUseCase<CreateSpaceCommand, SpaceResultDto>>(),
                Is.SameAs(create));
            Assert.That(secondScope.ServiceProvider.GetRequiredService<IUseCase<ListSpacesQuery, SpacePageDto>>(),
                Is.Not.SameAs(list));
            Assert.That(secondScope.ServiceProvider.GetRequiredService<IUseCase<GetSpaceQuery, SpaceResultDto>>(),
                Is.Not.SameAs(get));
            Assert.That(secondScope.ServiceProvider.GetRequiredService<IUseCase<CreateSpaceCommand, SpaceResultDto>>(),
                Is.Not.SameAs(create));
        });
    }

    private static User CreateAccount(CurrentActor actor) =>
        User.Reconstitute(
            actor.UserId,
            actor.UserPublicId,
            EmailAddress.Create("space-owner@example.com").Value,
            NormalizedName.Create("space-owner").Value,
            "Space Owner",
            "hashed-password",
            actor.SystemRole,
            UserStatus.Active,
            1,
            1,
            Now.AddDays(-1),
            Now,
            null,
            null);

    private static SpaceDetailsSnapshot CreateSpaceSnapshot(SpaceStatus status)
    {
        var space = Space.Reconstitute(7, Guid.CreateVersion7(), 21, "Product", null, status,
            null, 0, 0, 1, Now, Now, status == SpaceStatus.Archived ? Now : null, null, null);
        return new SpaceDetailsSnapshot(space, Guid.CreateVersion7());
    }

    private static AuthorizedSpaceContext AuthorizeOwner(SpaceDetailsSnapshot snapshot)
    {
        var space = snapshot.Space;
        var actor = CurrentActor.ForSession(
            space.OwnerUserId, snapshot.OwnerUserPublicId, SystemRole.User, Guid.CreateVersion7());
        var query = new SpaceAuthorizationQuery(actor, space.PublicId, [SpacePermission.SpaceUpdate], [space.Status]);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, new SpaceAuthorizationSnapshot(space, null, [])).Value;
    }

    private sealed class UnavailableSpaceDetailsReader : ISpaceDetailsReader
    {
        public Task<Result<SpaceDetailsSnapshot?>> ReadAsync(
            long spaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail<SpaceDetailsSnapshot?>(CommonError.DependencyUnavailable()));
    }
}
