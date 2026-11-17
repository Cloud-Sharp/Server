using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SpaceRepositoryIntegrationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 17, 1, 2, 3, TimeSpan.Zero);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_space_repository_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString(),
            })
            .Build();
        services = new ServiceCollection().AddPostgreSqlPersistence(configuration).BuildServiceProvider();

        await using var scope = CreateScope();
        await scope.Context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task FindByIdAsync_WithPersistedSpace_ShouldReturnFullSnapshot()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();

        var result = await scope.SpaceRepository.FindByIdAsync(seeded.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.EqualTo(seeded).UsingPropertiesComparer());
    }

    [Test]
    public async Task FindByPublicIdAsync_WithPersistedSpace_ShouldReturnFullSnapshot()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();

        var result = await scope.SpaceRepository.FindByPublicIdAsync(seeded.PublicId);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.EqualTo(seeded).UsingPropertiesComparer());
    }

    [Test]
    public async Task FindByIdAsync_WithUnknownId_ShouldReturnNull()
    {
        await using var scope = CreateScope();

        var result = await scope.SpaceRepository.FindByIdAsync(-1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindByPublicIdAsync_WithUnknownPublicId_ShouldReturnNull()
    {
        await using var scope = CreateScope();

        var result = await scope.SpaceRepository.FindByPublicIdAsync(Guid.CreateVersion7());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindQueries_AfterSnapshotMutation_ShouldReturnIndependentUnchangedSnapshots()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();

        var first = await scope.SpaceRepository.FindByIdAsync(seeded.Id);
        var second = await scope.SpaceRepository.FindByPublicIdAsync(seeded.PublicId);
        Assert.That(first.Value, Is.Not.SameAs(second.Value));

        var mutation = first.Value!.UpdateMetadata("Mutated snapshot", null, Timestamp.AddHours(1));
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());

        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(second.Value!.Name, Is.EqualTo(seeded.Name));
            Assert.That(second.Value.Description, Is.EqualTo(seeded.Description));
            Assert.That(reloaded.Name, Is.EqualTo(seeded.Name));
            Assert.That(reloaded.Description, Is.EqualTo(seeded.Description));
            Assert.That(reloaded.Version, Is.EqualTo(seeded.Version));
        });
    }

    [Test]
    public async Task AddAsync_AroundSave_ShouldDeferPersistAndReloadGeneratedIdWithoutMutatingInput()
    {
        var ownerId = await SeedOwnerAsync();
        var created = Space.Create(ownerId, "New Space", "Description", 4_096, Timestamp).Value;
        await using var scope = CreateScope();

        var add = await scope.SpaceRepository.AddAsync(created);
        Assert.Multiple(() =>
        {
            Assert.That(add.IsSuccess, Is.True, add.ToString());
            Assert.That(created.Id, Is.Zero);
        });

        await using (var probeScope = CreateScope())
        {
            var beforeSave = await probeScope.SpaceRepository.FindByPublicIdAsync(created.PublicId);
            Assert.Multiple(() =>
            {
                Assert.That(beforeSave.IsSuccess, Is.True, beforeSave.ToString());
                Assert.That(beforeSave.Value, Is.Null);
            });
        }

        var saved = await scope.Executor.ExecuteAsync<Space?>(async ct =>
        {
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (save.IsFailed)
            {
                return Result.Fail<Space?>(save.Errors);
            }

            return await scope.SpaceRepository.FindByPublicIdAsync(created.PublicId, ct);
        });

        Assert.That(saved.IsSuccess, Is.True, saved.ToString());
        Assert.That(saved.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Value!.Id, Is.GreaterThan(0));
            Assert.That(saved.Value.PublicId, Is.EqualTo(created.PublicId));
            Assert.That(created.Id, Is.Zero);
        });

        await using var committedScope = CreateScope();
        var persisted = await ReloadAsync(committedScope, created.PublicId);
        Assert.That(persisted.Id, Is.EqualTo(saved.Value!.Id));
    }

    [Test]
    public async Task UpdateAsync_WithoutSave_ShouldNotModifyDatabase()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();
        var space = await ReloadAsync(scope, seeded.PublicId);
        var expectedVersion = space.Version;
        var mutation = space.UpdateMetadata("Staged only", "Staged description", Timestamp.AddHours(1));
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());

        var update = await scope.SpaceRepository.UpdateAsync(space, expectedVersion);
        Assert.That(update.IsSuccess, Is.True, update.ToString());

        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Name, Is.EqualTo(seeded.Name));
            Assert.That(persisted.Description, Is.EqualTo(seeded.Description));
            Assert.That(persisted.Version, Is.EqualTo(seeded.Version));
        });
    }

    [Test]
    public async Task UpdateAsync_WhenClearingDescription_ShouldPersistNull()
    {
        var seeded = await SeedSpaceAsync(description: "Description to clear");
        var updatedAt = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await PersistMutationAsync(
            scope,
            seeded.PublicId,
            space => space.UpdateMetadata(space.Name, null, updatedAt));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Description, Is.Null);
            Assert.That(persisted.Version, Is.EqualTo(seeded.Version + 1));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(updatedAt));
        });
    }

    [Test]
    public async Task UpdateAsync_TwiceInSameScope_ShouldReuseTrackedEntityAndPersistBothChanges()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();

        var first = await ReloadAsync(scope, seeded.PublicId);
        var firstExpectedVersion = first.Version;
        var firstMutation = first.UpdateMetadata("First", "First description", Timestamp.AddHours(1));
        Assert.That(firstMutation.IsSuccess, Is.True, firstMutation.ToString());
        await scope.SpaceRepository.UpdateAsync(first, firstExpectedVersion);
        var tracked = scope.Context.Spaces.Local.Single(entity => entity.Id == seeded.Id);
        var firstSave = await scope.UnitOfWork.SaveChangesAsync();
        Assert.That(firstSave.IsSuccess, Is.True, firstSave.ToString());

        var second = await ReloadAsync(scope, seeded.PublicId);
        var secondExpectedVersion = second.Version;
        var secondMutation = second.UpdateMetadata("Second", "Second description", Timestamp.AddHours(2));
        Assert.That(secondMutation.IsSuccess, Is.True, secondMutation.ToString());
        await scope.SpaceRepository.UpdateAsync(second, secondExpectedVersion);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Context.Spaces.Local.Single(entity => entity.Id == seeded.Id), Is.SameAs(tracked));
            Assert.That(scope.Context.Spaces.Local.Count(entity => entity.Id == seeded.Id), Is.EqualTo(1));
        });

        var secondSave = await scope.UnitOfWork.SaveChangesAsync();
        Assert.That(secondSave.IsSuccess, Is.True, secondSave.ToString());

        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Name, Is.EqualTo("Second"));
            Assert.That(persisted.Description, Is.EqualTo("Second description"));
            Assert.That(persisted.Version, Is.EqualTo(seeded.Version + 2));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(Timestamp.AddHours(2)));
        });
    }

    [Test]
    public async Task UpdateAsync_OnExternallyChangedVersion_ShouldFailWithPreconditionFailed()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();
        var stale = await ReloadAsync(scope, seeded.PublicId);
        var expectedVersion = stale.Version;
        await SaveCompetingUpdateAsync(seeded.PublicId, "Competitor", Timestamp.AddHours(1));

        var mutation = stale.UpdateMetadata("Stale write", "Stale description", Timestamp.AddHours(2));
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());
        var update = await scope.SpaceRepository.UpdateAsync(stale, expectedVersion);
        Assert.That(update.IsSuccess, Is.True, update.ToString());

        var save = await scope.UnitOfWork.SaveChangesAsync();

        AssertError(save, ErrorCodes.Common.PreconditionFailed);
        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Name, Is.EqualTo("Competitor"));
            Assert.That(persisted.Version, Is.EqualTo(seeded.Version + 1));
        });
    }

    [Test]
    public async Task UpdateAsync_OnStaleTrackedSpaceWithoutDomainChange_ShouldFailWithPreconditionFailed()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();
        var tracked = await scope.Context.Spaces.SingleAsync(entity => entity.PublicId == seeded.PublicId);
        var stale = tracked.ToDomain();
        await SaveCompetingUpdateAsync(seeded.PublicId, "Competitor", Timestamp.AddHours(1));

        var update = await scope.SpaceRepository.UpdateAsync(stale, stale.Version);
        Assert.That(update.IsSuccess, Is.True, update.ToString());
        Assert.That(
            scope.Context.Entry(tracked).Property(entity => entity.Version).IsModified,
            Is.True);

        var save = await scope.UnitOfWork.SaveChangesAsync();

        AssertError(save, ErrorCodes.Common.PreconditionFailed);
        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Name, Is.EqualTo("Competitor"));
            Assert.That(persisted.Version, Is.EqualTo(seeded.Version + 1));
        });
    }

    [Test]
    public async Task UpdateAsync_WithUngeneratedId_ShouldThrow()
    {
        await using var scope = CreateScope();
        var unsaved = Space.Create(1, "Unsaved", null, null, Timestamp).Value;

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.SpaceRepository.UpdateAsync(unsaved, expectedVersion: 1));
        Assert.That(scope.Context.Spaces.Local, Is.Empty);
    }

    [Test]
    public async Task WriteMethods_WithNullSpace_ShouldThrow()
    {
        await using var scope = CreateScope();

        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.SpaceRepository.AddAsync(null!));
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.SpaceRepository.UpdateAsync(null!, expectedVersion: 1));
        Assert.That(scope.Context.Spaces.Local, Is.Empty);
    }

    [Test]
    public async Task WriteMethods_WithCancelledToken_ShouldThrowBeforeStaging()
    {
        await using var scope = CreateScope();
        var space = Space.Create(1, "Cancelled", null, null, Timestamp).Value;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.SpaceRepository.AddAsync(space, cancelled.Token));
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.SpaceRepository.UpdateAsync(space, 1, cancelled.Token));
        Assert.That(scope.Context.Spaces.Local, Is.Empty);
    }

    private RepositoryScope CreateScope() => new(services!.CreateAsyncScope());

    private async Task<long> SeedOwnerAsync()
    {
        await using var scope = CreateScope();
        return await SaveOwnerAsync(scope.Context);
    }

    private async Task<Space> SeedSpaceAsync(string? description = "Initial description")
    {
        await using var scope = CreateScope();
        var ownerId = await SaveOwnerAsync(scope.Context);
        var space = Space.Create(ownerId, $"Space {Guid.NewGuid():N}", description, 4_096, Timestamp).Value;
        scope.Context.Spaces.Add(SpaceEntity.FromDomain(space));
        await scope.Context.SaveChangesAsync();

        var persisted = await scope.Context.Spaces.AsNoTracking()
            .SingleAsync(entity => entity.PublicId == space.PublicId);
        return persisted.ToDomain();
    }

    private static async Task<long> SaveOwnerAsync(AppDbContext context)
    {
        var owner = UserEntity.FromDomain(CreateUser());
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        return owner.Id;
    }

    private async Task SaveCompetingUpdateAsync(Guid publicId, string name, DateTimeOffset updatedAt)
    {
        await using var competitorScope = CreateScope();
        var result = await PersistMutationAsync(
            competitorScope,
            publicId,
            space => space.UpdateMetadata(name, space.Description, updatedAt));
        Assert.That(result.IsSuccess, Is.True, result.ToString());
    }

    private static Task<Result> PersistMutationAsync(
        RepositoryScope scope,
        Guid publicId,
        Func<Space, Result> mutate)
    {
        return scope.Executor.ExecuteAsync(async ct =>
        {
            var found = await scope.SpaceRepository.FindByPublicIdAsync(publicId, ct);
            if (found.IsFailed)
            {
                return Result.Fail(found.Errors);
            }

            if (found.Value is null)
            {
                return Result.Fail(new Error("Space was not found."));
            }

            var space = found.Value;
            var expectedVersion = space.Version;
            var mutation = mutate(space);
            if (mutation.IsFailed)
            {
                return mutation;
            }

            var update = await scope.SpaceRepository.UpdateAsync(space, expectedVersion, ct);
            if (update.IsFailed)
            {
                return update;
            }

            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });
    }

    private static async Task<Space> ReloadAsync(RepositoryScope scope, Guid publicId)
    {
        var reloaded = await scope.SpaceRepository.FindByPublicIdAsync(publicId);
        Assert.That(reloaded.IsSuccess, Is.True, reloaded.ToString());
        Assert.That(reloaded.Value, Is.Not.Null);
        return reloaded.Value!;
    }

    private static User CreateUser() =>
        User.Create(
            $"space-owner-{Guid.NewGuid():N}@example.com",
            null,
            "Space Owner",
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value;

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        var hasCode = result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string value && value == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }

    private sealed class RepositoryScope(AsyncServiceScope serviceScope) : IAsyncDisposable
    {
        public AppDbContext Context => serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();

        public ISpaceRepository SpaceRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceRepository>();

        public IUnitOfWork UnitOfWork => serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public ITransactionExecutor Executor =>
            serviceScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => serviceScope.DisposeAsync();
    }
}
