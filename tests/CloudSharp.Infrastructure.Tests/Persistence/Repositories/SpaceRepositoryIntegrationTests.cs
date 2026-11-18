using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Spaces.Creations;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using CloudSharp.TestSupport.Fakes;
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
        services = new ServiceCollection()
            .AddSingleton<ITokenHasher>(new HmacSha256TokenHasher(new TokenHashingOptions
            {
                ActiveKey = Convert.ToBase64String(new byte[32]),
            }))
            .AddPostgreSqlPersistence(configuration)
            .BuildServiceProvider();

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

    [Test]
    public async Task DetailsReadAsync_ShouldReturnCurrentSpaceAndOwnerPublicId()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();
        var ownerPublicId = await scope.Context.Users.AsNoTracking()
            .Where(user => user.Id == seeded.OwnerUserId)
            .Select(user => user.PublicId)
            .SingleAsync();

        var result = await scope.DetailsReader.ReadAsync(seeded.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Space, Is.EqualTo(seeded).UsingPropertiesComparer());
            Assert.That(result.Value.OwnerUserPublicId, Is.EqualTo(ownerPublicId));
        });
    }

    [Test]
    public async Task DetailsReadAsync_ShouldReturnNullForMissingSpace()
    {
        await using var scope = CreateScope();

        var result = await scope.DetailsReader.ReadAsync(-1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task DetailsReadAsync_ShouldReturnDetachedIndependentSnapshots()
    {
        var seeded = await SeedSpaceAsync();
        await using var scope = CreateScope();

        var first = await scope.DetailsReader.ReadAsync(seeded.Id);
        var second = await scope.DetailsReader.ReadAsync(seeded.Id);
        Assert.That(first.Value, Is.Not.Null);
        Assert.That(second.Value, Is.Not.Null);
        Assert.That(first.Value!.Space, Is.Not.SameAs(second.Value!.Space));

        var mutation = first.Value.Space.UpdateMetadata("Changed snapshot", null, Timestamp.AddHours(1));
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());
        var reloaded = await scope.DetailsReader.ReadAsync(seeded.Id);

        Assert.Multiple(() =>
        {
            Assert.That(second.Value.Space.Name, Is.EqualTo(seeded.Name));
            Assert.That(reloaded.Value!.Space.Name, Is.EqualTo(seeded.Name));
            Assert.That(reloaded.Value.Space.Version, Is.EqualTo(seeded.Version));
            Assert.That(scope.Context.Spaces.Local, Is.Empty);
        });
    }

    [Test]
    public async Task AuthorizationReadAsync_ShouldReturnNullForMissingSpace()
    {
        await using var scope = CreateScope();

        var result = await scope.AuthorizationReader.ReadAsync(Guid.CreateVersion7(), userId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Null);
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(MembershipAbsence.OtherUser)]
    [TestCase(MembershipAbsence.LeftHistory)]
    public async Task AuthorizationReadAsync_ShouldReturnSpaceWithoutCurrentMembership(
        MembershipAbsence absence)
    {
        var owner = await SeedUserAsync();
        var requester = await SeedUserAsync();
        var space = await SeedSpaceAsync(ownerId: owner.Id, createdAt: Timestamp.AddDays(30));
        var membershipUser = absence == MembershipAbsence.OtherUser ? await SeedUserAsync() : requester;
        var status = absence == MembershipAbsence.OtherUser
            ? SpaceMembershipStatus.Active
            : SpaceMembershipStatus.Left;
        await using (var seedScope = CreateScope())
        {
            var membership = CreateMembershipEntity(space.Id, membershipUser.Id, status, space.CreatedAt);
            seedScope.Context.SpaceMemberships.Add(membership);
            SpaceRoleEntity? role = null;
            if (status == SpaceMembershipStatus.Active)
            {
                role = SpaceRoleEntity.FromDomain(
                    SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Viewer, space.CreatedAt).Value);
                seedScope.Context.SpaceRoles.Add(role);
            }

            await seedScope.Context.SaveChangesAsync();
            if (role is not null)
            {
                seedScope.Context.SpaceMembershipRoles.Add(
                    SpaceMembershipRoleEntity.FromDomain(
                        membership,
                        MembershipRoleAssignment.Create(role.Id, owner.Id, space.CreatedAt)));
                await seedScope.Context.SaveChangesAsync();
            }
        }

        await using var scope = CreateScope();
        var result = await scope.AuthorizationReader.ReadAsync(space.PublicId, requester.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Space, Is.EqualTo(space).UsingPropertiesComparer());
            Assert.That(result.Value.Membership, Is.Null);
            Assert.That(result.Value.Roles, Is.Empty);
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task AuthorizationReadAsync_ShouldRestoreOnlyCurrentMembershipAssignmentsAndRoles(
        SpaceMembershipStatus membershipStatus)
    {
        var owner = await SeedUserAsync();
        var requester = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var createdAt = Timestamp.AddDays(31);
        var space = await SeedSpaceAsync(ownerId: owner.Id, createdAt: createdAt);
        SpaceRoleEntity currentRole;
        SpaceRoleEntity deletedRole;
        SpaceRoleEntity leftRole;
        SpaceRoleEntity otherRole;
        SpaceMembershipEntity currentMembership;
        await using (var seedScope = CreateScope())
        {
            currentRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Member, createdAt).Value);
            deletedRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Viewer, createdAt).Value);
            deletedRole.DeletedAt = createdAt;
            leftRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Admin, createdAt).Value);
            leftRole.DeletedAt = createdAt;
            otherRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Admin, createdAt).Value);
            currentMembership = CreateMembershipEntity(space.Id, requester.Id, membershipStatus, createdAt);
            currentMembership.UpdatedAt = createdAt.AddMinutes(2);
            var leftHistory = CreateMembershipEntity(
                space.Id, requester.Id, SpaceMembershipStatus.Left, createdAt);
            var otherMembership = CreateMembershipEntity(
                space.Id, otherUser.Id, SpaceMembershipStatus.Active, createdAt);
            seedScope.Context.SpaceRoles.AddRange(currentRole, deletedRole, leftRole, otherRole);
            seedScope.Context.SpaceMemberships.AddRange(currentMembership, leftHistory, otherMembership);
            await seedScope.Context.SaveChangesAsync();
            seedScope.Context.SpaceMembershipRoles.AddRange(
                SpaceMembershipRoleEntity.FromDomain(
                    currentMembership,
                    MembershipRoleAssignment.Create(currentRole.Id, owner.Id, createdAt.AddMinutes(1))),
                SpaceMembershipRoleEntity.FromDomain(
                    currentMembership,
                    MembershipRoleAssignment.Create(deletedRole.Id, otherUser.Id, createdAt.AddMinutes(2))),
                SpaceMembershipRoleEntity.FromDomain(
                    leftHistory,
                    MembershipRoleAssignment.Create(leftRole.Id, owner.Id, createdAt)),
                SpaceMembershipRoleEntity.FromDomain(
                    otherMembership,
                    MembershipRoleAssignment.Create(otherRole.Id, owner.Id, createdAt)));
            await seedScope.Context.SaveChangesAsync();
        }

        await using var scope = CreateScope();
        var result = await scope.AuthorizationReader.ReadAsync(space.PublicId, requester.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        var membership = result.Value!.Membership;
        Assert.That(membership, Is.Not.Null);
        var currentAssignment = membership!.RoleAssignments.Single(
            assignment => assignment.RoleId == currentRole.Id);
        var deletedAssignment = membership.RoleAssignments.Single(
            assignment => assignment.RoleId == deletedRole.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Space, Is.EqualTo(space).UsingPropertiesComparer());
            Assert.That(membership.Id, Is.EqualTo(currentMembership.Id));
            Assert.That(membership.PublicId, Is.EqualTo(currentMembership.PublicId));
            Assert.That(membership.UserId, Is.EqualTo(requester.Id));
            Assert.That(membership.Status, Is.EqualTo(membershipStatus));
            Assert.That(membership.UpdatedAt, Is.EqualTo(createdAt.AddMinutes(2)));
            Assert.That(membership.SuspendedAt,
                Is.EqualTo(membershipStatus == SpaceMembershipStatus.Suspended ? createdAt : (DateTimeOffset?)null));
            Assert.That(membership.LeftAt, Is.Null);
            Assert.That(membership.RoleAssignments.Select(assignment => assignment.RoleId),
                Is.EquivalentTo(new[] { currentRole.Id, deletedRole.Id }));
            Assert.That(currentAssignment.AssignedByUserId, Is.EqualTo(owner.Id));
            Assert.That(currentAssignment.AssignedAt, Is.EqualTo(createdAt.AddMinutes(1)));
            Assert.That(deletedAssignment.AssignedByUserId, Is.EqualTo(otherUser.Id));
            Assert.That(deletedAssignment.AssignedAt, Is.EqualTo(createdAt.AddMinutes(2)));
            Assert.That(result.Value.Roles.Select(role => role.Id),
                Is.EquivalentTo(new[] { currentRole.Id, deletedRole.Id }));
            Assert.That(result.Value.Roles.Single(role => role.Id == deletedRole.Id).DeletedAt,
                Is.EqualTo(createdAt));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public async Task AuthorizationReadAsync_ShouldReturnFreshIndependentSnapshotsAfterRoleChange()
    {
        var owner = await SeedUserAsync();
        var requester = await SeedUserAsync();
        var createdAt = Timestamp.AddDays(32);
        var space = await SeedSpaceAsync(ownerId: owner.Id, createdAt: createdAt);
        SpaceRoleEntity role;
        await using (var seedScope = CreateScope())
        {
            role = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Viewer, createdAt).Value);
            var membership = CreateMembershipEntity(
                space.Id, requester.Id, SpaceMembershipStatus.Active, createdAt);
            seedScope.Context.SpaceRoles.Add(role);
            seedScope.Context.SpaceMemberships.Add(membership);
            await seedScope.Context.SaveChangesAsync();
            seedScope.Context.SpaceMembershipRoles.Add(
                SpaceMembershipRoleEntity.FromDomain(
                    membership,
                    MembershipRoleAssignment.Create(role.Id, owner.Id, createdAt)));
            await seedScope.Context.SaveChangesAsync();
        }

        await using var scope = CreateScope();
        var first = await scope.AuthorizationReader.ReadAsync(space.PublicId, requester.Id);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        var firstRole = first.Value!.Roles.Single();
        Assert.That(firstRole.StoredPermissions, Does.Not.Contain(SpacePermission.FilesWrite));

        await using (var updateScope = CreateScope())
        {
            var persistedRole = await updateScope.Context.SpaceRoles.SingleAsync(entity => entity.Id == role.Id);
            persistedRole.PermissionMask |= 1L << ((int)SpacePermission.FilesWrite - 1);
            persistedRole.Version++;
            persistedRole.UpdatedAt = createdAt.AddMinutes(1);
            await updateScope.Context.SaveChangesAsync();
        }

        var second = await scope.AuthorizationReader.ReadAsync(space.PublicId, requester.Id);

        Assert.That(second.IsSuccess, Is.True, second.ToString());
        var secondRole = second.Value!.Roles.Single();
        Assert.Multiple(() =>
        {
            Assert.That(first.Value.Space, Is.Not.SameAs(second.Value.Space));
            Assert.That(first.Value.Membership, Is.Not.SameAs(second.Value.Membership));
            Assert.That(firstRole, Is.Not.SameAs(secondRole));
            Assert.That(firstRole.StoredPermissions, Does.Not.Contain(SpacePermission.FilesWrite));
            Assert.That(secondRole.StoredPermissions, Does.Contain(SpacePermission.FilesWrite));
            Assert.That(firstRole.Version, Is.EqualTo(1));
            Assert.That(secondRole.Version, Is.EqualTo(2));
            Assert.That(firstRole.UpdatedAt, Is.EqualTo(createdAt));
            Assert.That(secondRole.UpdatedAt, Is.EqualTo(createdAt.AddMinutes(1)));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public async Task ListReadAsync_ShouldPaginateBidirectionallyAcrossEqualTimestamps()
    {
        var owner = await SeedUserAsync();
        var createdAt = Timestamp.AddDays(40);
        var seeded = new List<Space>();
        for (var index = 0; index < 5; index++)
        {
            seeded.Add(await SeedSpaceAsync(ownerId: owner.Id, createdAt: createdAt));
        }

        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();

        var descending = await ReadAllCandidatesAsync(scope, actor, descending: true, batchSize: 2);
        var ascending = await ReadAllCandidatesAsync(scope, actor, descending: false, batchSize: 2);

        var descendingIds = descending.Select(candidate => candidate.Authorization.Space.PublicId).ToArray();
        var ascendingIds = ascending.Select(candidate => candidate.Authorization.Space.PublicId).ToArray();
        var seededIds = seeded.Select(space => space.PublicId).ToHashSet();
        var expectedDescending = seeded.OrderByDescending(space => space.PublicId)
            .Select(space => space.PublicId)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ascendingIds, Is.EqualTo(descendingIds.Reverse()));
            Assert.That(descendingIds.Where(seededIds.Contains), Is.EqualTo(expectedDescending));
            Assert.That(ascendingIds.Where(seededIds.Contains), Is.EqualTo(expectedDescending.Reverse()));
            Assert.That(descendingIds, Is.Unique);
            Assert.That(ascendingIds, Is.Unique);
        });
    }

    [Test]
    public async Task ListReadAsync_ShouldReplayCursorAfterBoundarySpaceDeletion()
    {
        var owner = await SeedUserAsync();
        var createdAt = new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var seeded = new List<Space>();
        for (var index = 0; index < 3; index++)
        {
            seeded.Add(await SeedSpaceAsync(ownerId: owner.Id, createdAt: createdAt));
        }

        var expected = seeded.OrderByDescending(space => space.PublicId).ToArray();
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();
        var first = await scope.ListCandidateReader.ReadAsync(actor, null, 1, null, descending: true);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        Assert.That(first.Value.Candidates, Has.Count.EqualTo(1));
        var boundary = first.Value.Candidates[0];
        Assert.That(boundary.Authorization.Space.PublicId, Is.EqualTo(expected[0].PublicId));

        await scope.Context.Spaces
            .Where(space => space.Id == boundary.Authorization.Space.Id)
            .ExecuteDeleteAsync();
        var replayed = await scope.ListCandidateReader.ReadAsync(
            actor, boundary.CursorAfter, 1, null, descending: true);

        Assert.That(replayed.IsSuccess, Is.True, replayed.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(replayed.Value.Candidates, Has.Count.EqualTo(1));
            Assert.That(replayed.Value.Candidates[0].Authorization.Space.PublicId,
                Is.EqualTo(expected[1].PublicId));
            Assert.That(replayed.Value.Candidates[0].CursorAfter, Is.Not.EqualTo(boundary.CursorAfter));
        });
    }

    [Test]
    public async Task ListReadAsync_ShouldRejectTamperedCursor()
    {
        var owner = await SeedUserAsync();
        await SeedSpaceAsync(ownerId: owner.Id);
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();
        var page = await scope.ListCandidateReader.ReadAsync(actor, null, 1, null, descending: true);
        var cursor = page.Value.Candidates[0].CursorAfter;
        var last = cursor[^1] == 'A' ? 'B' : 'A';
        var tampered = cursor[..^1] + last;

        var result = await scope.ListCandidateReader.ReadAsync(actor, tampered, 1, null, descending: true);

        AssertError(result, ErrorCodes.Space.ListCursorInvalid, "PropertyName", "Cursor");
    }

    [Test]
    public async Task ListReadAsync_ShouldRejectMismatchedCursorScopeOrDirection()
    {
        var owner = await SeedUserAsync();
        await SeedSpaceAsync(ownerId: owner.Id);
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();
        var page = await scope.ListCandidateReader.ReadAsync(actor, null, 1, null, descending: true);
        var cursor = page.Value.Candidates[0].CursorAfter;

        var directionMismatch = await scope.ListCandidateReader.ReadAsync(
            actor, cursor, 1, null, descending: false);
        var userMismatch = await scope.ListCandidateReader.ReadAsync(
            CurrentActor.ForSession(actor.UserId, Guid.CreateVersion7(), actor.SystemRole, Guid.CreateVersion7()),
            cursor, 1, null, descending: true);
        var roleMismatch = await scope.ListCandidateReader.ReadAsync(
            CurrentActor.ForSession(actor.UserId, actor.UserPublicId, SystemRole.SystemAdmin, Guid.CreateVersion7()),
            cursor, 1, null, descending: true);
        var typeMismatch = await scope.ListCandidateReader.ReadAsync(
            CurrentActor.ForMcp(actor.UserId, actor.UserPublicId, actor.SystemRole, 99, Guid.CreateVersion7()),
            cursor, 1, null, descending: true);

        foreach (var result in new[] { directionMismatch, userMismatch, roleMismatch, typeMismatch })
        {
            AssertError(result, ErrorCodes.Space.ListCursorInvalid, "PropertyName", "Cursor");
        }
    }

    [TestCase("name")]
    [TestCase("created_at")]
    public async Task ListReadAsync_ShouldRejectUnsupportedSort(string sort)
    {
        var owner = await SeedUserAsync();
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();

        var result = await scope.ListCandidateReader.ReadAsync(actor, null, 1, sort, descending: true);

        AssertError(result, ErrorCodes.Space.ListSortInvalid, "PropertyName", "Sort");
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task ListReadAsync_ShouldRestoreCurrentMembershipAssignmentsAndRoles(
        SpaceMembershipStatus membershipStatus)
    {
        var owner = await SeedUserAsync();
        var member = await SeedUserAsync();
        var createdAt = Timestamp.AddDays(20);
        var space = await SeedSpaceAsync(ownerId: owner.Id, createdAt: createdAt);
        SpaceMembershipEntity currentMembership;
        SpaceRoleEntity activeRole;
        SpaceRoleEntity deletedRole;
        await using (var seedScope = CreateScope())
        {
            activeRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Member, createdAt).Value);
            deletedRole = SpaceRoleEntity.FromDomain(
                SpaceRole.CreateManaged(space.Id, SpaceRoleSystemKey.Viewer, createdAt).Value);
            deletedRole.DeletedAt = createdAt;
            currentMembership = CreateMembershipEntity(space.Id, member.Id, membershipStatus, createdAt);
            var leftHistory = CreateMembershipEntity(
                space.Id, member.Id, SpaceMembershipStatus.Left, createdAt);
            seedScope.Context.SpaceRoles.AddRange(activeRole, deletedRole);
            seedScope.Context.SpaceMemberships.AddRange(leftHistory, currentMembership);
            await seedScope.Context.SaveChangesAsync();
            seedScope.Context.SpaceMembershipRoles.AddRange(
                SpaceMembershipRoleEntity.FromDomain(
                    currentMembership,
                    MembershipRoleAssignment.Create(activeRole.Id, owner.Id, createdAt)),
                SpaceMembershipRoleEntity.FromDomain(
                    currentMembership,
                    MembershipRoleAssignment.Create(deletedRole.Id, owner.Id, createdAt)));
            await seedScope.Context.SaveChangesAsync();
        }

        var actor = CurrentActor.ForSession(member.Id, member.PublicId, SystemRole.User, Guid.CreateVersion7());
        await using var scope = CreateScope();
        var candidates = await ReadAllCandidatesAsync(scope, actor, descending: true, batchSize: 5);

        var candidate = candidates.Single(item => item.Authorization.Space.Id == space.Id);
        var membership = candidate.Authorization.Membership;
        Assert.That(membership, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(candidate.OwnerUserPublicId, Is.EqualTo(owner.PublicId));
            Assert.That(membership!.Id, Is.EqualTo(currentMembership.Id));
            Assert.That(membership.PublicId, Is.EqualTo(currentMembership.PublicId));
            Assert.That(membership.Status, Is.EqualTo(membershipStatus));
            Assert.That(membership.RoleAssignments.Select(assignment => assignment.RoleId),
                Is.EquivalentTo(new[] { activeRole.Id, deletedRole.Id }));
            Assert.That(candidate.Authorization.Roles.Select(role => role.Id),
                Is.EquivalentTo(new[] { activeRole.Id, deletedRole.Id }));
            Assert.That(candidate.Authorization.Roles.Single(role => role.Id == deletedRole.Id).DeletedAt,
                Is.EqualTo(createdAt));
        });
    }

    [Test]
    public async Task BootstrapAddAsync_ShouldStageAndCommitManagedRolePermissionMasks()
    {
        var space = await SeedSpaceAsync();
        var roles = new[]
        {
            SpaceRoleSystemKey.Admin,
            SpaceRoleSystemKey.Member,
            SpaceRoleSystemKey.Viewer,
        }.Select(key => SpaceRole.CreateManaged(space.Id, key, space.CreatedAt).Value).ToArray();
        await using var scope = CreateScope();

        var stagedResult = await scope.BootstrapWriter.AddAsync(space.Id, roles, space.CreatedAt);

        Assert.That(stagedResult.IsSuccess, Is.True, stagedResult.ToString());
        var staged = scope.Context.SpaceRoles.Local.Where(role => role.SpaceId == space.Id).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(staged, Has.Length.EqualTo(3));
            Assert.That(staged.Select(role => scope.Context.Entry(role).State), Is.All.EqualTo(EntityState.Added));
            Assert.That(staged.Single(role => role.SystemKey == SpaceRoleSystemKey.Admin).PermissionMask,
                Is.EqualTo(8191L));
            Assert.That(staged.Single(role => role.SystemKey == SpaceRoleSystemKey.Member).PermissionMask,
                Is.EqualTo(765L));
            Assert.That(staged.Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer).PermissionMask,
                Is.EqualTo(21L));
            Assert.That(roles.Select(role => role.Id), Is.All.Zero);
        });
        await using (var probe = CreateScope())
        {
            Assert.That(await probe.Context.SpaceRoles.CountAsync(role => role.SpaceId == space.Id), Is.Zero);
        }

        var committed = await scope.Executor.ExecuteAsync(ct => scope.UnitOfWork.SaveChangesAsync(ct));

        Assert.That(committed.IsSuccess, Is.True, committed.ToString());
        await using var committedProbe = CreateScope();
        var saved = await committedProbe.Context.SpaceRoles.AsNoTracking()
            .Where(role => role.SpaceId == space.Id)
            .ToArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved, Has.Length.EqualTo(3));
            Assert.That(saved.Single(role => role.SystemKey == SpaceRoleSystemKey.Admin).PermissionMask,
                Is.EqualTo(8191L));
            Assert.That(saved.Single(role => role.SystemKey == SpaceRoleSystemKey.Member).PermissionMask,
                Is.EqualTo(765L));
            Assert.That(saved.Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer).PermissionMask,
                Is.EqualTo(21L));
            Assert.That(roles.Select(role => role.Id), Is.All.Zero);
        });
    }

    [Test]
    public async Task BootstrapAddAsync_ShouldRejectMismatchedRolesWithoutStaging()
    {
        var space = await SeedSpaceAsync();
        var otherSpaceRole = SpaceRole.CreateManaged(space.Id + 1, SpaceRoleSystemKey.Admin, space.CreatedAt).Value;
        var otherTimeRole = SpaceRole.CreateManaged(
            space.Id, SpaceRoleSystemKey.Member, space.CreatedAt.AddSeconds(1)).Value;
        await using var scope = CreateScope();

        var spaceMismatch = await scope.BootstrapWriter.AddAsync(
            space.Id, [otherSpaceRole], space.CreatedAt);
        var timeMismatch = await scope.BootstrapWriter.AddAsync(
            space.Id, [otherTimeRole], space.CreatedAt);

        AssertError(spaceMismatch, ErrorCodes.SpaceRole.InvalidState);
        AssertError(timeMismatch, ErrorCodes.SpaceRole.InvalidState);
        Assert.That(scope.Context.SpaceRoles.Local, Is.Empty);
    }

    [Test]
    public async Task CreateSpace_ShouldRollbackSpaceWhenSecondSaveFails()
    {
        var owner = await SeedUserAsync();
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, SystemRole.User, Guid.CreateVersion7());
        var name = $"Rollback {Guid.NewGuid():N}";
        await using var scope = CreateScope();
        var failingUnitOfWork = new FailSecondSaveUnitOfWork(scope.UnitOfWork);
        var useCase = new CreateSpaceUseCase(
            scope.UserRepository,
            scope.SpaceRepository,
            scope.BootstrapWriter,
            failingUnitOfWork,
            scope.Executor,
            new FakeClock(Timestamp.AddDays(120)));

        var result = await useCase.ExecuteAsync(new CreateSpaceCommand(actor, name, null, 1024));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(failingUnitOfWork.CallCount, Is.EqualTo(2));
        await using var probe = CreateScope();
        Assert.That(await probe.Context.Spaces.AnyAsync(space => space.OwnerUserId == owner.Id && space.Name == name),
            Is.False);
    }

    private static async Task<List<SpaceListCandidate>> ReadAllCandidatesAsync(
        RepositoryScope scope,
        CurrentActor actor,
        bool descending,
        int batchSize)
    {
        var candidates = new List<SpaceListCandidate>();
        var traversedCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        while (true)
        {
            var result = await scope.ListCandidateReader.ReadAsync(
                actor, cursor, batchSize, null, descending);
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            var page = result.Value;
            Assert.That(page.Candidates.Count, Is.LessThanOrEqualTo(batchSize));
            Assert.That(page.Candidates.Select(candidate => candidate.CursorAfter),
                Is.All.Not.Null.And.Not.Empty);
            candidates.AddRange(page.Candidates);
            if (!page.HasMore)
            {
                return candidates;
            }

            Assert.That(page.Candidates, Is.Not.Empty);
            cursor = page.Candidates[^1].CursorAfter;
            Assert.That(traversedCursors.Add(cursor), Is.True, "Space cursor did not advance.");
        }
    }

    private RepositoryScope CreateScope() => new(services!.CreateAsyncScope());

    private async Task<long> SeedOwnerAsync()
    {
        await using var scope = CreateScope();
        return await SaveOwnerAsync(scope.Context);
    }

    private async Task<User> SeedUserAsync()
    {
        await using var scope = CreateScope();
        var user = UserEntity.FromDomain(CreateUser());
        scope.Context.Users.Add(user);
        await scope.Context.SaveChangesAsync();
        return user.ToDomain();
    }

    private async Task<Space> SeedSpaceAsync(
        string? description = "Initial description",
        long? ownerId = null,
        DateTimeOffset? createdAt = null)
    {
        await using var scope = CreateScope();
        var actualOwnerId = ownerId ?? await SaveOwnerAsync(scope.Context);
        var space = Space.Create(
            actualOwnerId,
            $"Space {Guid.NewGuid():N}",
            description,
            4_096,
            createdAt ?? Timestamp).Value;
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

    private static SpaceMembershipEntity CreateMembershipEntity(
        long spaceId,
        long userId,
        SpaceMembershipStatus status,
        DateTimeOffset timestamp) =>
        new()
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            UserId = userId,
            Status = status,
            Version = 1,
            JoinedAt = timestamp,
            UpdatedAt = timestamp,
            SuspendedAt = status == SpaceMembershipStatus.Suspended ? timestamp : null,
            LeftAt = status == SpaceMembershipStatus.Left ? timestamp : null,
        };

    private static void AssertError(
        ResultBase result,
        string expectedCode,
        string? metadataKey = null,
        object? expectedMetadataValue = null)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        var matchingErrors = result.Errors.Where(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string value && value == expectedCode).ToArray();
        Assert.That(matchingErrors, Is.Not.Empty, $"Expected error code {expectedCode} in metadata.");
        if (metadataKey is not null)
        {
            Assert.That(matchingErrors[0].Metadata[metadataKey], Is.EqualTo(expectedMetadataValue));
        }
    }

    public enum MembershipAbsence
    {
        OtherUser,
        LeftHistory,
    }

    private sealed class FailSecondSaveUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public int CallCount { get; private set; }

        public Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount == 2
                ? Task.FromResult(Result.Fail<int>(CommonError.DependencyUnavailable()))
                : inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class RepositoryScope(AsyncServiceScope serviceScope) : IAsyncDisposable
    {
        public AppDbContext Context => serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();

        public IUserRepository UserRepository =>
            serviceScope.ServiceProvider.GetRequiredService<IUserRepository>();

        public ISpaceRepository SpaceRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceRepository>();

        public ISpaceDetailsReader DetailsReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceDetailsReader>();

        public ISpaceListCandidateReader ListCandidateReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceListCandidateReader>();

        public ISpaceAuthorizationReader AuthorizationReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceAuthorizationReader>();

        public ISpaceBootstrapWriter BootstrapWriter =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceBootstrapWriter>();

        public IUnitOfWork UnitOfWork => serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public ITransactionExecutor Executor =>
            serviceScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => serviceScope.DisposeAsync();
    }
}
