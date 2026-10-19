using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// 실제 PostgreSQL 17 컨테이너에서 <c>UserRepository</c>의 조회·staging·동시성·장애 계약을
/// DI 등록 그대로 검증한다. 저장은 UnitOfWork, commit/rollback은 TransactionExecutor가 담당한다.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class UserRepositoryIntegrationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 6, 12, 34, 56, 789, TimeSpan.Zero);

    private const string UnreachableConnectionString =
        "Host=localhost;Port=1;Database=cloudsharp;Username=cloudsharp;Password=cloudsharp";

    private PostgreSqlContainer? postgres;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_user_repository_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        await using var scope = CreateScope();
        await scope.Context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task RegisterFlow_InsideTransaction_ShouldReloadGeneratedIdAfterSave()
    {
        var created = CreateUser(UniqueUserName());
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            var exists = await scope.UserRepository.ExistsByNormalizedEmailAsync(created.Email.Normalized, ct);
            if (exists.IsFailed)
            {
                return Result.Fail(exists.Errors);
            }

            if (exists.Value)
            {
                return Result.Fail(UserError.EmailConflict());
            }

            await scope.UserRepository.AddAsync(created, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (save.IsFailed)
            {
                return Result.Fail(save.Errors);
            }

            var reload = await scope.UserRepository.FindByPublicIdAsync(created.PublicId, ct);
            if (reload.IsFailed)
            {
                return Result.Fail(reload.Errors);
            }

            var saved = reload.Value;
            return saved is null || saved.Id <= 0 || saved.PublicId != created.PublicId
                ? Result.Fail(new Error("Reloaded user lacks a generated id or has a mismatched identity."))
                : Result.Ok();
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(created.Id, Is.Zero);
        });

        await using var probeScope = CreateScope();
        var persisted = await ReloadAsync(probeScope, created.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Id, Is.GreaterThan(0));
            Assert.That(persisted.PublicId, Is.EqualTo(created.PublicId));
            Assert.That(persisted.UserName!.Value, Is.EqualTo(created.UserName!.Value));
        });
    }

    [Test]
    public async Task AddAsync_WithoutSave_ShouldNotPersistOrMutateInput()
    {
        var created = CreateUser();
        await using var scope = CreateScope();

        var add = await scope.UserRepository.AddAsync(created);

        Assert.Multiple(() =>
        {
            Assert.That(add.IsSuccess, Is.True);
            Assert.That(created.Id, Is.Zero);
        });

        await using var probeScope = CreateScope();
        var exists = await probeScope.UserRepository.ExistsByNormalizedEmailAsync(created.Email.Normalized);
        var reloaded = await probeScope.UserRepository.FindByPublicIdAsync(created.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(exists.IsSuccess, Is.True);
            Assert.That(exists.Value, Is.False);
            Assert.That(reloaded.Value, Is.Null);
        });

        var save = await scope.UnitOfWork.SaveChangesAsync();
        Assert.That(save.IsSuccess, Is.True);

        await using var committedScope = CreateScope();
        var persisted = await ReloadAsync(committedScope, created.PublicId);
        Assert.That(persisted.Id, Is.GreaterThan(0));
    }

    [Test]
    public async Task UpdateAsync_WithoutSave_ShouldNotModifyDatabase()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        var found = await scope.UserRepository.FindByPublicIdAsync(seeded.PublicId);
        Assert.That(found.IsSuccess, Is.True, found.ToString());
        var user = found.Value!;
        var expectedVersion = user.Version;
        var change = user.ChangeProfile("Staged Only", Timestamp.AddHours(1));
        Assert.That(change.IsSuccess, Is.True);

        var update = await scope.UserRepository.UpdateAsync(user, expectedVersion);
        Assert.That(update.IsSuccess, Is.True);

        await using var probeScope = CreateScope();
        var probed = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(probed.DisplayName, Is.EqualTo(seeded.DisplayName));
            Assert.That(probed.Version, Is.EqualTo(seeded.Version));
        });
    }

    [Test]
    public async Task AddAsync_OnMultipleUsersWithSingleSave_ShouldPersistAllWithDistinctIds()
    {
        var first = CreateUser();
        var second = CreateUser();
        var third = CreateUser(UniqueUserName());
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(first, ct);
            await scope.UserRepository.AddAsync(second, ct);
            await scope.UserRepository.AddAsync(third, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        Assert.That(result.IsSuccess, Is.True, result.ToString());

        await using var probeScope = CreateScope();
        var firstPersisted = await ReloadAsync(probeScope, first.PublicId);
        var secondPersisted = await ReloadAsync(probeScope, second.PublicId);
        var thirdPersisted = await ReloadAsync(probeScope, third.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(firstPersisted.Id, Is.GreaterThan(0));
            Assert.That(secondPersisted.Id, Is.GreaterThan(0));
            Assert.That(thirdPersisted.Id, Is.GreaterThan(0));
            Assert.That(firstPersisted.Id, Is.Not.EqualTo(secondPersisted.Id));
            Assert.That(secondPersisted.Id, Is.Not.EqualTo(thirdPersisted.Id));
            Assert.That(firstPersisted.UserName, Is.Null);
            Assert.That(secondPersisted.UserName, Is.Null);
            Assert.That(thirdPersisted.UserName, Is.Not.Null);
        });
    }

    [Test]
    public async Task FindByNormalizedEmailAsync_WithPersistedUser_ShouldReturnMatchingSnapshot()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByNormalizedEmailAsync(seeded.Email.Normalized);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Id, Is.EqualTo(seeded.Id));
            Assert.That(result.Value.PublicId, Is.EqualTo(seeded.PublicId));
            Assert.That(result.Value.Email.Value, Is.EqualTo(seeded.Email.Value));
            Assert.That(result.Value.Email.Normalized, Is.EqualTo(seeded.Email.Normalized));
        });
    }

    [Test]
    public async Task FindByNormalizedEmailAsync_WithUnknownEmail_ShouldReturnNull()
    {
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByNormalizedEmailAsync(
            $"missing-{Guid.NewGuid():N}@EXAMPLE.COM");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindByIdAsync_WithPersistedUser_ShouldReturnFullSnapshot()
    {
        var created = CreateUser();
        var seeded = await RegisterUserAsync(created);
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByIdAsync(seeded.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        var user = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(user.Id, Is.EqualTo(seeded.Id));
            Assert.That(user.PublicId, Is.EqualTo(created.PublicId));
            Assert.That(user.Email.Value, Is.EqualTo(created.Email.Value));
            Assert.That(user.Email.Normalized, Is.EqualTo(created.Email.Normalized));
            Assert.That(user.UserName, Is.Null);
            Assert.That(user.DisplayName, Is.EqualTo(created.DisplayName));
            Assert.That(user.PasswordHash, Is.EqualTo(created.PasswordHash));
            Assert.That(user.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.SecurityVersion, Is.EqualTo(1));
            Assert.That(user.Version, Is.EqualTo(1));
            Assert.That(user.CreatedAt, Is.EqualTo(Timestamp));
            Assert.That(user.UpdatedAt, Is.EqualTo(Timestamp));
            Assert.That(user.SuspendedAt, Is.Null);
            Assert.That(user.DeletedAt, Is.Null);
        });
    }

    [Test]
    public async Task FindByIdAsync_WithUnknownId_ShouldReturnNull()
    {
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByIdAsync(-1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindByPublicIdAsync_WithPersistedUser_ShouldReturnMatchingSnapshot()
    {
        var seeded = await RegisterUserAsync(CreateUser(UniqueUserName()));
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByPublicIdAsync(seeded.PublicId);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Id, Is.EqualTo(seeded.Id));
            Assert.That(result.Value.UserName!.Value, Is.EqualTo(seeded.UserName!.Value));
        });
    }

    [Test]
    public async Task FindByPublicIdAsync_WithUnknownPublicId_ShouldReturnNull()
    {
        await using var scope = CreateScope();

        var result = await scope.UserRepository.FindByPublicIdAsync(Guid.CreateVersion7());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task ExistsByNormalizedEmailAsync_WithPersistedUser_ShouldReturnTrue()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        var result = await scope.UserRepository.ExistsByNormalizedEmailAsync(seeded.Email.Normalized);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.True);
        });
    }

    [Test]
    public async Task ExistsByNormalizedEmailAsync_WithUnknownEmail_ShouldReturnFalse()
    {
        await using var scope = CreateScope();

        var result = await scope.UserRepository.ExistsByNormalizedEmailAsync(
            $"missing-{Guid.NewGuid():N}@EXAMPLE.COM");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.False);
        });
    }

    [Test]
    public async Task ExistsByNormalizedUserNameAsync_WithPersistedUser_ShouldReturnTrue()
    {
        var seeded = await RegisterUserAsync(CreateUser(UniqueUserName()));
        await using var scope = CreateScope();

        var result = await scope.UserRepository.ExistsByNormalizedUserNameAsync(seeded.UserName!.Normalized);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.True);
        });
    }

    [Test]
    public async Task ExistsByNormalizedUserNameAsync_WithUnknownUserName_ShouldReturnFalse()
    {
        await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        var result = await scope.UserRepository.ExistsByNormalizedUserNameAsync(
            UniqueUserName().ToUpperInvariant());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.False);
        });
    }

    [Test]
    public async Task FindQueries_ShouldReturnIndependentSnapshots()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        var first = await scope.UserRepository.FindByIdAsync(seeded.Id);
        var second = await scope.UserRepository.FindByIdAsync(seeded.Id);
        Assert.That(first.Value, Is.Not.SameAs(second.Value));

        var mutated = first.Value!.ChangeProfile("Mutated Snapshot", Timestamp.AddHours(1));
        Assert.That(mutated.IsSuccess, Is.True);

        var reloaded = await scope.UserRepository.FindByIdAsync(seeded.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Value!.DisplayName, Is.EqualTo(seeded.DisplayName));
            Assert.That(reloaded.Value!.Version, Is.EqualTo(seeded.Version));
        });
    }

    [Test]
    public async Task UpdateAsync_OnSuspend_ShouldPersistSuspendedState()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var suspendedAt = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await PersistMutationAsync(scope, seeded.PublicId, user => user.Suspend(suspendedAt));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(UserStatus.Suspended));
            Assert.That(reloaded.SuspendedAt, Is.EqualTo(suspendedAt));
            Assert.That(reloaded.DeletedAt, Is.Null);
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(2));
            Assert.That(reloaded.Version, Is.EqualTo(2));
            Assert.That(reloaded.UpdatedAt, Is.EqualTo(suspendedAt));
        });
    }

    [Test]
    public async Task UpdateAsync_OnActivate_ShouldPersistActiveStateAndClearSuspendedAt()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var suspendedAt = Timestamp.AddHours(1);
        var activatedAt = Timestamp.AddHours(2);
        await using var scope = CreateScope();

        var suspended = await PersistMutationAsync(scope, seeded.PublicId, user => user.Suspend(suspendedAt));
        Assert.That(suspended.IsSuccess, Is.True, suspended.ToString());

        var activated = await PersistMutationAsync(scope, seeded.PublicId, user => user.Activate(activatedAt));
        Assert.That(activated.IsSuccess, Is.True, activated.ToString());

        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(reloaded.SuspendedAt, Is.Null);
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(2));
            Assert.That(reloaded.Version, Is.EqualTo(3));
            Assert.That(reloaded.UpdatedAt, Is.EqualTo(activatedAt));
        });
    }

    [Test]
    public async Task UpdateAsync_OnDeleteFromSuspended_ShouldPersistDeletedStateWithoutSuspendedAt()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var suspendedAt = Timestamp.AddHours(1);
        var deletedAt = Timestamp.AddHours(2);
        await using var scope = CreateScope();

        var suspended = await PersistMutationAsync(scope, seeded.PublicId, user => user.Suspend(suspendedAt));
        Assert.That(suspended.IsSuccess, Is.True, suspended.ToString());

        var deleted = await PersistMutationAsync(scope, seeded.PublicId, user => user.Delete(deletedAt));
        Assert.That(deleted.IsSuccess, Is.True, deleted.ToString());

        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(UserStatus.Deleted));
            Assert.That(reloaded.SuspendedAt, Is.Null);
            Assert.That(reloaded.DeletedAt, Is.EqualTo(deletedAt));
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(3));
            Assert.That(reloaded.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task UpdateAsync_OnDeleteFromActive_ShouldPersistDeletedState()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var deletedAt = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await PersistMutationAsync(scope, seeded.PublicId, user => user.Delete(deletedAt));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(UserStatus.Deleted));
            Assert.That(reloaded.DeletedAt, Is.EqualTo(deletedAt));
            Assert.That(reloaded.SuspendedAt, Is.Null);
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(2));
            Assert.That(reloaded.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateAsync_OnChangePasswordHash_ShouldPersistNewHashAndBumpSecurityVersion()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var changedAt = Timestamp.AddHours(1);
        const string newHash = "pbkdf2-sha256$iterations=100000$changed";
        await using var scope = CreateScope();

        var result = await PersistMutationAsync(
            scope, seeded.PublicId, user => user.ChangePasswordHash(newHash, changedAt));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.PasswordHash, Is.EqualTo(newHash));
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(2));
            Assert.That(reloaded.Version, Is.EqualTo(2));
            Assert.That(reloaded.UpdatedAt, Is.EqualTo(changedAt));
        });
    }

    [Test]
    public async Task UpdateAsync_OnChangeSystemRole_ShouldPersistSystemAdminRole()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var changedAt = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await PersistMutationAsync(
            scope, seeded.PublicId, user => user.ChangeSystemRole(SystemRole.SystemAdmin, changedAt));

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        var reloaded = await ReloadAsync(scope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SystemRole, Is.EqualTo(SystemRole.SystemAdmin));
            Assert.That(reloaded.SecurityVersion, Is.EqualTo(1));
            Assert.That(reloaded.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateAsync_TwiceInSameTransaction_ShouldPersistBothChanges()
    {
        var created = CreateUser();
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(created, ct);
            var firstSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (firstSave.IsFailed)
            {
                return Result.Fail(firstSave.Errors);
            }

            var firstReload = await scope.UserRepository.FindByPublicIdAsync(created.PublicId, ct);
            var firstUser = firstReload.Value!;
            var firstVersion = firstUser.Version;
            firstUser.ChangeProfile("First", Timestamp.AddHours(1));
            await scope.UserRepository.UpdateAsync(firstUser, firstVersion, ct);
            var secondSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (secondSave.IsFailed)
            {
                return Result.Fail(secondSave.Errors);
            }

            var secondReload = await scope.UserRepository.FindByPublicIdAsync(created.PublicId, ct);
            var secondUser = secondReload.Value!;
            var secondVersion = secondUser.Version;
            secondUser.ChangeProfile("Second", Timestamp.AddHours(2));
            await scope.UserRepository.UpdateAsync(secondUser, secondVersion, ct);
            var thirdSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            return thirdSave.IsSuccess ? Result.Ok() : Result.Fail(thirdSave.Errors);
        });

        Assert.That(result.IsSuccess, Is.True, result.ToString());

        await using var probeScope = CreateScope();
        var reloaded = await ReloadAsync(probeScope, created.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.DisplayName, Is.EqualTo("Second"));
            Assert.That(reloaded.Version, Is.EqualTo(3));
            Assert.That(reloaded.UpdatedAt, Is.EqualTo(Timestamp.AddHours(2)));
        });
    }

    [Test]
    public async Task UpdateAsync_OnSecondSaveWithStaleExpectedVersion_ShouldFailWithPreconditionFailed()
    {
        var created = CreateUser();
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(created, ct);
            var firstSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (firstSave.IsFailed)
            {
                return Result.Fail(firstSave.Errors);
            }

            var firstReload = await scope.UserRepository.FindByPublicIdAsync(created.PublicId, ct);
            var user = firstReload.Value!;
            var currentVersion = user.Version;
            user.ChangeProfile("First", Timestamp.AddHours(1));
            await scope.UserRepository.UpdateAsync(user, currentVersion, ct);
            var secondSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (secondSave.IsFailed)
            {
                return Result.Fail(secondSave.Errors);
            }

            var secondReload = await scope.UserRepository.FindByPublicIdAsync(created.PublicId, ct);
            await scope.UserRepository.UpdateAsync(secondReload.Value!, expectedVersion: 1, ct);
            var thirdSave = await scope.UnitOfWork.SaveChangesAsync(ct);
            return thirdSave.IsSuccess ? Result.Ok() : Result.Fail(thirdSave.Errors);
        });

        AssertError(result, ErrorCodes.Common.PreconditionFailed);

        await using var probeScope = CreateScope();
        var reloaded = await probeScope.UserRepository.FindByPublicIdAsync(created.PublicId);
        Assert.That(reloaded.Value, Is.Null, "The whole transaction including the insert must roll back.");
    }

    [Test]
    public async Task UpdateAsync_OnExternallyChangedVersion_ShouldFailWithPreconditionFailed()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var later = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            var found = await scope.UserRepository.FindByPublicIdAsync(seeded.PublicId, ct);
            var user = found.Value!;
            var expectedVersion = user.Version;

            await BumpVersionExternallyAsync(seeded.PublicId, later, ct);

            user.ChangeProfile("Stale Write", later);
            await scope.UserRepository.UpdateAsync(user, expectedVersion, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        AssertError(result, ErrorCodes.Common.PreconditionFailed);

        await using var probeScope = CreateScope();
        var reloaded = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.DisplayName, Is.EqualTo(seeded.DisplayName));
            Assert.That(reloaded.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateAsync_OnExternallyChangedVersionWithoutDomainChange_ShouldFailWithPreconditionFailed()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var later = Timestamp.AddHours(1);
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            var found = await scope.UserRepository.FindByPublicIdAsync(seeded.PublicId, ct);
            var user = found.Value!;
            var expectedVersion = user.Version;

            await BumpVersionExternallyAsync(seeded.PublicId, later, ct);

            await scope.UserRepository.UpdateAsync(user, expectedVersion, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        AssertError(result, ErrorCodes.Common.PreconditionFailed);

        await using var probeScope = CreateScope();
        var reloaded = await ReloadAsync(probeScope, seeded.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.DisplayName, Is.EqualTo(seeded.DisplayName));
            Assert.That(reloaded.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateAsync_OnUnknownUser_ShouldFailWithPreconditionFailedAtSave()
    {
        var ghost = CreateGhostUser(id: 999_999);
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.UpdateAsync(ghost, expectedVersion: 1, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        AssertError(result, ErrorCodes.Common.PreconditionFailed);
    }

    [Test]
    public async Task UpdateAsync_WithUngeneratedId_ShouldThrow()
    {
        await using var scope = CreateScope();
        var unsaved = CreateUser();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.UserRepository.UpdateAsync(unsaved, expectedVersion: 1));
    }

    [Test]
    public async Task AddAsync_OnDuplicateNormalizedEmail_ShouldFailWithEmailConflict()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        var duplicate = User.Create(
            seeded.Email.Value.ToUpperInvariant(),
            UniqueUserName(),
            "Duplicate Email",
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value;
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(duplicate, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        AssertError(result, ErrorCodes.User.EmailConflict);

        await using var probeScope = CreateScope();
        var exists = await probeScope.UserRepository.ExistsByNormalizedEmailAsync(seeded.Email.Normalized);
        Assert.That(exists.Value, Is.True);
        var reloaded = await probeScope.UserRepository.FindByNormalizedEmailAsync(seeded.Email.Normalized);
        Assert.That(reloaded.Value!.Id, Is.EqualTo(seeded.Id));
    }

    [Test]
    public async Task AddAsync_OnDuplicateNormalizedUserName_ShouldFailWithNameConflict()
    {
        var seeded = await RegisterUserAsync(CreateUser("shared-name"));
        var duplicate = User.Create(
            $"user-{Guid.NewGuid():N}@example.com",
            "SHARED-NAME",
            "Duplicate Name",
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value;
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(duplicate, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });

        AssertError(result, ErrorCodes.User.NameConflict);

        await using var probeScope = CreateScope();
        var reloaded = await probeScope.UserRepository.ExistsByNormalizedUserNameAsync(seeded.UserName!.Normalized);
        Assert.That(reloaded.Value, Is.True);
    }

    [Test]
    [TestCaseSource(nameof(UnavailableReadOperationCases))]
    public async Task ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(
        Func<IUserRepository, CancellationToken, Task<ResultBase>> read)
    {
        await using var scope = CreateScope(UnreachableConnectionString);

        var result = await read(scope.UserRepository, CancellationToken.None);

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
    }

    [Test]
    public async Task FindByIdAsync_WithCancelledToken_ShouldThrow()
    {
        await using var scope = CreateScope();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.UserRepository.FindByIdAsync(1, cancelled.Token));
    }

    [Test]
    public async Task AddAsync_WithCancelledToken_ShouldThrow()
    {
        await using var scope = CreateScope();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.UserRepository.AddAsync(CreateUser(), cancelled.Token));
    }

    [Test]
    public async Task FindByIdAsync_OnCorruptedStatusTimestamp_ShouldThrow()
    {
        var seeded = await RegisterUserAsync(CreateUser());
        await using var scope = CreateScope();

        await scope.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET suspended_at = {Timestamp} WHERE public_id = {seeded.PublicId}");

        Assert.ThrowsAsync<ArgumentException>(
            async () => await scope.UserRepository.FindByIdAsync(seeded.Id));
    }

    public static IEnumerable<TestCaseData> UnavailableReadOperationCases()
    {
        yield return new TestCaseData(
                (Func<IUserRepository, CancellationToken, Task<ResultBase>>)(
                    async (repository, cancellationToken) =>
                        await repository.ExistsByNormalizedEmailAsync("PROBE@EXAMPLE.COM", cancellationToken)))
            .SetName("ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(ExistsByNormalizedEmailAsync)");
        yield return new TestCaseData(
                (Func<IUserRepository, CancellationToken, Task<ResultBase>>)(
                    async (repository, cancellationToken) =>
                        await repository.ExistsByNormalizedUserNameAsync("PROBE", cancellationToken)))
            .SetName("ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(ExistsByNormalizedUserNameAsync)");
        yield return new TestCaseData(
                (Func<IUserRepository, CancellationToken, Task<ResultBase>>)(
                    async (repository, cancellationToken) =>
                        await repository.FindByNormalizedEmailAsync("PROBE@EXAMPLE.COM", cancellationToken)))
            .SetName("ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(FindByNormalizedEmailAsync)");
        yield return new TestCaseData(
                (Func<IUserRepository, CancellationToken, Task<ResultBase>>)(
                    async (repository, cancellationToken) =>
                        await repository.FindByIdAsync(1, cancellationToken)))
            .SetName("ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(FindByIdAsync)");
        yield return new TestCaseData(
                (Func<IUserRepository, CancellationToken, Task<ResultBase>>)(
                    async (repository, cancellationToken) =>
                        await repository.FindByPublicIdAsync(Guid.CreateVersion7(), cancellationToken)))
            .SetName("ReadQueries_OnUnavailablePostgres_ShouldReturnDependencyUnavailable(FindByPublicIdAsync)");
    }

    private RepositoryScope CreateScope(string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString ?? postgres!.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);
        var provider = services.BuildServiceProvider();
        var serviceScope = provider.CreateAsyncScope();
        return new RepositoryScope(provider, serviceScope);
    }

    private async Task<User> RegisterUserAsync(User user)
    {
        await using var scope = CreateScope();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            await scope.UserRepository.AddAsync(user, ct);
            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            if (save.IsFailed)
            {
                return Result.Fail<User>(save.Errors);
            }

            var reload = await scope.UserRepository.FindByPublicIdAsync(user.PublicId, ct);
            if (reload.IsFailed)
            {
                return Result.Fail<User>(reload.Errors);
            }

            var saved = reload.Value;
            return saved is null || saved.Id <= 0
                ? Result.Fail<User>(new Error("Registered user was not reloadable."))
                : Result.Ok(saved);
        });

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        return result.Value;
    }

    private static Task<Result> PersistMutationAsync(
        RepositoryScope scope,
        Guid publicId,
        Action<User> mutate)
    {
        return scope.Executor.ExecuteAsync(async ct =>
        {
            var found = await scope.UserRepository.FindByPublicIdAsync(publicId, ct);
            if (found.IsFailed)
            {
                return Result.Fail(found.Errors);
            }

            var user = found.Value!;
            var expectedVersion = user.Version;
            mutate(user);
            var update = await scope.UserRepository.UpdateAsync(user, expectedVersion, ct);
            if (update.IsFailed)
            {
                return Result.Fail(update.Errors);
            }

            var save = await scope.UnitOfWork.SaveChangesAsync(ct);
            return save.IsSuccess ? Result.Ok() : Result.Fail(save.Errors);
        });
    }

    private async Task BumpVersionExternallyAsync(
        Guid publicId,
        DateTimeOffset later,
        CancellationToken cancellationToken)
    {
        await using var competitorScope = CreateScope();
        await competitorScope.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET version = version + 1, updated_at = {later} WHERE public_id = {publicId}",
            cancellationToken);
    }

    private static async Task<User> ReloadAsync(RepositoryScope scope, Guid publicId)
    {
        var reloaded = await scope.UserRepository.FindByPublicIdAsync(publicId);
        Assert.That(reloaded.IsSuccess, Is.True, reloaded.ToString());
        Assert.That(reloaded.Value, Is.Not.Null);
        return reloaded.Value!;
    }

    private static User CreateUser(string? userName = null, string? displayName = "Alice Wonderland") =>
        User.Create(
            $"user-{Guid.NewGuid():N}@example.com",
            userName,
            displayName,
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value;

    private static string UniqueUserName() => $"u{Guid.NewGuid():N}"[..21];

    private static User CreateGhostUser(long id)
    {
        var email = $"ghost-{Guid.NewGuid():N}@example.com";
        return User.Reconstitute(
            id: id,
            publicId: Guid.CreateVersion7(),
            email: EmailAddress.Reconstitute(email, email.ToUpperInvariant()),
            userName: null,
            displayName: "Ghost",
            passwordHash: "pbkdf2-sha256$iterations=100000$ghost",
            systemRole: SystemRole.User,
            status: UserStatus.Active,
            securityVersion: 1,
            version: 1,
            createdAt: Timestamp,
            updatedAt: Timestamp,
            suspendedAt: null,
            deletedAt: null);
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        var hasCode = result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string value && value == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }

    private sealed class RepositoryScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope serviceScope;

        public RepositoryScope(ServiceProvider provider, AsyncServiceScope serviceScope)
        {
            this.provider = provider;
            this.serviceScope = serviceScope;
        }

        public AppDbContext Context => Services.GetRequiredService<AppDbContext>();

        public IUserRepository UserRepository => Services.GetRequiredService<IUserRepository>();

        public IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();

        public ITransactionExecutor Executor => Services.GetRequiredService<ITransactionExecutor>();

        private IServiceProvider Services => serviceScope.ServiceProvider;

        public async ValueTask DisposeAsync()
        {
            await serviceScope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}