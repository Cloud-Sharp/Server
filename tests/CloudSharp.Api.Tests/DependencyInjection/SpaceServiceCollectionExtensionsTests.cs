using CloudSharp.Api.DependencyInjection;
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
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.MetadataUpdates;
using CloudSharp.Core.UseCases.Spaces.Reactivations;
using CloudSharp.TestSupport.Fakes;
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
}
