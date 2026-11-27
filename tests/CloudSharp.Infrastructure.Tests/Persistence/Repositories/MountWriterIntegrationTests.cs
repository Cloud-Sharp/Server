using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Dapper;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class MountWriterIntegrationTests
{
    private const long SpaceId = 100;
    private const long OtherSpaceId = 200;
    private const long ForeignRoleId = 4;
    private const long View = 1L << 14;
    private const long Manage = 1L << 13;
    private const long ViewManage = View | Manage;
    private const long FilesWrite = 1L << 3;
    private const long SeededVersion = 7;
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 26, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = CreatedAt.AddHours(1);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_mount_writer_tests")
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
            .AddPostgreSqlPersistence(configuration)
            .BuildServiceProvider();

        await using var scope = CreateScope();
        await scope.Context.Database.EnsureCreatedAsync();

        // Each test uses its own Mount. These shared grant targets are read-only.
        await using var sql = new NpgsqlConnection(postgres.GetConnectionString());
        await sql.ExecuteAsync(SeedGrantTargetsSql, new { SpaceId, OtherSpaceId, ForeignRoleId, CreatedAt });
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
    public async Task AddAsync_ShouldStageMountAndInitialRoleAndUserGrantsForCommit()
    {
        var mount = CreateMount(1);
        await using var scope = CreateScope();
        var writer = scope.Services.GetRequiredService<IMountCreationWriter>();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            AssertSuccess(await writer.AddAsync(mount, ct));

            // Query within the write transaction so a premature save would be visible.
            Assert.That(await FindMountAsync(scope.Context, mount.PublicId, ct), Is.Null);
            var saved = await scope.UnitOfWork.SaveChangesAsync(ct);
            return saved.ToResult();
        });

        AssertSuccess(result);
        await using var probe = CreateScope();
        var persisted = await FindMountAsync(probe.Context, mount.PublicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Id, Is.Positive);
            Assert.That(persisted.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(persisted.Name, Is.EqualTo("New mount"));
            Assert.That(persisted.Visibility, Is.EqualTo(MountVisibility.Private));
            Assert.That(persisted.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(persisted.Version, Is.EqualTo(1));
            Assert.That(persisted.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(CreatedAt));
            Assert.That(persisted.ArchivedAt, Is.Null);
        });
        await AssertGrantsAsync(probe.Context, persisted!.Id, [(1, ViewManage, 0L)], [(1, Manage, 0L)]);
        Assert.That(mount.Id, Is.Zero);
    }

    [Test]
    public async Task AddAsync_WithRoleFromAnotherSpace_ShouldRollbackCreation()
    {
        var mount = CreateMount(ForeignRoleId);
        await using var scope = CreateScope();
        var writer = scope.Services.GetRequiredService<IMountCreationWriter>();

        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            AssertSuccess(await writer.AddAsync(mount, ct));
            var saved = await scope.UnitOfWork.SaveChangesAsync(ct);
            return saved.ToResult();
        });

        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.OfType<DbConstraintViolationError>().Select(error => error.ConstraintName),
            Is.EqualTo(new[] { "fk_mount_role_grants_role" }));
        await using var probe = CreateScope();
        Assert.That(await FindMountAsync(probe.Context, mount.PublicId), Is.Null);
    }

    [Test]
    public async Task UpdateAsync_ShouldCommitMountChangesAndSynchronizeBothGrantTables()
    {
        var publicId = await SeedMountAsync();
        var otherMountPublicId = await SeedMountAsync();
        await using var scope = CreateScope();
        var mount = await ReadSnapshotAsync(scope, publicId);
        var expectedVersion = mount.Version;
        AssertSuccess(mount.UpdateSettings("Renamed mount", MountVisibility.Private, ChangedAt));
        AssertSuccess(mount.Archive(ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 1,
            [SpacePermission.MountView, SpacePermission.MountManage], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 2, [], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(
            MountOverwriteTargetType.Role, 3, [SpacePermission.MountView], [SpacePermission.FilesWrite], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 1, [SpacePermission.MountView], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 2, [], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 3,
            [SpacePermission.MountView, SpacePermission.MountManage], [], ChangedAt));

        var writer = scope.Services.GetRequiredService<IMountMutationWriter>();
        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            AssertSuccess(await writer.UpdateAsync(mount, expectedVersion, ct));

            var beforeSave = await FindMountAsync(scope.Context, publicId, ct);
            Assert.That(beforeSave!.Version, Is.EqualTo(expectedVersion));
            await AssertSeededGrantsAsync(scope.Context, mount.Id, ct);
            var saved = await scope.UnitOfWork.SaveChangesAsync(ct);
            return saved.ToResult();
        });

        AssertSuccess(result);
        await using var probe = CreateScope();
        var persisted = await FindMountAsync(probe.Context, publicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Id, Is.EqualTo(mount.Id));
            Assert.That(persisted.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(persisted.Name, Is.EqualTo("Renamed mount"));
            Assert.That(persisted.Visibility, Is.EqualTo(MountVisibility.Private));
            Assert.That(persisted.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(persisted.Version, Is.EqualTo(mount.Version));
            Assert.That(persisted.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(ChangedAt));
            Assert.That(persisted.ArchivedAt, Is.EqualTo(ChangedAt));
        });
        await AssertGrantsAsync(
            probe.Context,
            mount.Id,
            [(1, ViewManage, 0L), (3, View, FilesWrite)],
            [(1, View, 0L), (3, ViewManage, 0L)]);

        var otherMount = await FindMountAsync(probe.Context, otherMountPublicId);
        Assert.That(otherMount!.Version, Is.EqualTo(SeededVersion));
        await AssertSeededGrantsAsync(probe.Context, otherMount.Id);
    }

    [Test]
    public async Task UpdateAsync_WithStaleVersion_ShouldRejectSaveAndPreserveCommittedRows()
    {
        var publicId = await SeedMountAsync();
        await using var scope = CreateScope();
        var mount = await ReadSnapshotAsync(scope, publicId);
        var expectedVersion = mount.Version;
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 1, [], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 3, [SpacePermission.MountView], [], ChangedAt));

        // Advance the database version before the writer has a chance to load a tracked row.
        var winnerUpdatedAt = ChangedAt.AddHours(1);
        await using (var competingScope = CreateScope())
        {
            var affected = await competingScope.Context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE mounts
                SET name = 'Concurrent rename', version = version + 1, updated_at = {winnerUpdatedAt}
                WHERE id = {mount.Id}
                """);
            Assert.That(affected, Is.EqualTo(1));
        }

        var writer = scope.Services.GetRequiredService<IMountMutationWriter>();
        var result = await scope.Executor.ExecuteAsync(async ct =>
        {
            AssertSuccess(await writer.UpdateAsync(mount, expectedVersion, ct));
            var saved = await scope.UnitOfWork.SaveChangesAsync(ct);
            return saved.ToResult();
        });

        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && Equals(code, ErrorCodes.Common.PreconditionFailed)), Is.True, result.ToString());
        await using var probe = CreateScope();
        var persisted = await FindMountAsync(probe.Context, publicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo("Concurrent rename"));
            Assert.That(persisted.Version, Is.EqualTo(expectedVersion + 1));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(winnerUpdatedAt));
        });
        await AssertSeededGrantsAsync(probe.Context, mount.Id);
    }

    [TestCase(MountOverwriteTargetType.Role)]
    [TestCase(MountOverwriteTargetType.User)]
    public async Task UpdateAsync_WithConcurrentGrantInsert_ShouldRejectLosingSave(MountOverwriteTargetType targetType)
    {
        var publicId = await SeedMountAsync();
        await using var winningScope = CreateScope();
        await using var losingScope = CreateScope();
        var winning = await ReadSnapshotAsync(winningScope, publicId);
        var losing = await ReadSnapshotAsync(losingScope, publicId);
        var expectedVersion = winning.Version;

        AssertSuccess(winning.SetOverwrite(targetType, 3,
            [SpacePermission.MountView, SpacePermission.MountManage], [], ChangedAt));
        AssertSuccess(losing.SetOverwrite(targetType, 3, [SpacePermission.MountView], [], ChangedAt));
        AssertSuccess(await winningScope.Services.GetRequiredService<IMountMutationWriter>()
            .UpdateAsync(winning, expectedVersion));
        AssertSuccess(await losingScope.Services.GetRequiredService<IMountMutationWriter>()
            .UpdateAsync(losing, expectedVersion));

        var winningSave = await winningScope.Executor.ExecuteAsync(
            ct => winningScope.UnitOfWork.SaveChangesAsync(ct));
        AssertSuccess(winningSave);
        var losingSave = await losingScope.Executor.ExecuteAsync(
            ct => losingScope.UnitOfWork.SaveChangesAsync(ct));

        AssertPreconditionFailed(losingSave);
        Assert.That(losingScope.Context.ChangeTracker.Entries(), Is.Empty);
        await using var probe = CreateScope();
        var persisted = await FindMountAsync(probe.Context, publicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Version, Is.EqualTo(winning.Version));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(ChangedAt));
        });
        var expectedRoles = new List<(long TargetId, long Allow, long Deny)> { (1, View, 0L), (2, ViewManage, 0L) };
        var expectedUsers = new List<(long TargetId, long Allow, long Deny)> { (1, Manage, 0L), (2, ViewManage, 0L) };
        if (targetType == MountOverwriteTargetType.Role)
        {
            expectedRoles.Add((3, ViewManage, 0L));
        }
        else
        {
            expectedUsers.Add((3, ViewManage, 0L));
        }

        await AssertGrantsAsync(probe.Context, winning.Id, expectedRoles.ToArray(), expectedUsers.ToArray());
    }

    [TestCase(MountOverwriteTargetType.Role)]
    [TestCase(MountOverwriteTargetType.User)]
    public async Task UpdateAsync_WhenMountWasDeletedBeforeGrantInsert_ShouldReturnPreconditionFailed(
        MountOverwriteTargetType targetType)
    {
        var publicId = await SeedMountAsync();
        await using var scope = CreateScope();
        var mount = await ReadSnapshotAsync(scope, publicId);
        var expectedVersion = mount.Version;
        AssertSuccess(mount.SetOverwrite(targetType, 3, [SpacePermission.MountView], [], ChangedAt));
        AssertSuccess(await scope.Services.GetRequiredService<IMountMutationWriter>()
            .UpdateAsync(mount, expectedVersion));

        await using (var deletingScope = CreateScope())
        {
            var deleted = await deletingScope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM mounts WHERE id = {mount.Id}");
            Assert.That(deleted, Is.EqualTo(1));
        }

        var save = await scope.Executor.ExecuteAsync(ct => scope.UnitOfWork.SaveChangesAsync(ct));

        AssertPreconditionFailed(save);
        Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        await using var probe = CreateScope();
        Assert.That(await FindMountAsync(probe.Context, publicId), Is.Null);
        Assert.That(await probe.Context.MountRoleGrants.AnyAsync(grant => grant.MountId == mount.Id), Is.False);
        Assert.That(await probe.Context.MountUserGrants.AnyAsync(grant => grant.MountId == mount.Id), Is.False);
    }

    [Test]
    public async Task UpdateAsync_WhenRestagedBeforeSave_ShouldReconcileFinalGrantSnapshot()
    {
        var publicId = await SeedMountAsync();
        await using var scope = CreateScope();
        var mount = await ReadSnapshotAsync(scope, publicId);
        var expectedVersion = mount.Version;
        var writer = scope.Services.GetRequiredService<IMountMutationWriter>();

        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 1,
            [SpacePermission.MountView, SpacePermission.MountManage], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 2,
            [SpacePermission.MountManage], [], ChangedAt));
        AssertSuccess(await writer.UpdateAsync(mount, expectedVersion));

        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 1, [], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 3, [SpacePermission.MountView], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 1, [], [], ChangedAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 3, [SpacePermission.MountView], [], ChangedAt));
        AssertSuccess(await writer.UpdateAsync(mount, expectedVersion));

        var finalAt = ChangedAt.AddHours(1);
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 1,
            [SpacePermission.MountView, SpacePermission.MountManage], [], finalAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.Role, 3, [], [], finalAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 1, [SpacePermission.MountManage], [], finalAt));
        AssertSuccess(mount.SetOverwrite(MountOverwriteTargetType.User, 3, [], [], finalAt));
        AssertSuccess(await writer.UpdateAsync(mount, expectedVersion));

        var stagedMount = scope.Context.Mounts.Local.Single();
        Assert.That(scope.Context.Entry(stagedMount).Property(entity => entity.Version).OriginalValue,
            Is.EqualTo(expectedVersion));
        var saved = await scope.UnitOfWork.SaveChangesAsync();
        AssertSuccess(saved);

        await using var probe = CreateScope();
        var persisted = await FindMountAsync(probe.Context, publicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Version, Is.EqualTo(mount.Version));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(finalAt));
        });
        await AssertGrantsAsync(
            probe.Context,
            mount.Id,
            [(1, ViewManage, 0L), (2, Manage, 0L)],
            [(1, Manage, 0L), (2, ViewManage, 0L)]);
    }

    private static Mount CreateMount(long roleId)
    {
        var role = MountPermissionOverwrite.Create(MountOverwriteTargetType.Role, roleId,
            [SpacePermission.MountView, SpacePermission.MountManage], []);
        var user = MountPermissionOverwrite.Create(MountOverwriteTargetType.User, 1, [SpacePermission.MountManage], []);
        AssertSuccess(role);
        AssertSuccess(user);

        var mount = Mount.Create(SpaceId, "New mount", MountVisibility.Private, CreatedAt, [role.Value, user.Value]);
        AssertSuccess(mount);
        return mount.Value;
    }

    private async Task<Guid> SeedMountAsync()
    {
        var publicId = Guid.CreateVersion7();
        await using var sql = new NpgsqlConnection(postgres!.GetConnectionString());
        await sql.ExecuteAsync("""
            WITH inserted_mount AS (
                INSERT INTO mounts (public_id, space_id, name, visibility, status, version, created_at, updated_at)
                VALUES (@PublicId, @SpaceId, 'Seeded mount', 'PUBLIC', 'ACTIVE', @SeededVersion, @CreatedAt, @CreatedAt)
                RETURNING id, space_id
            ), inserted_roles AS (
                INSERT INTO mount_role_grants (mount_id, space_id, role_id, allow_mask, deny_mask)
                SELECT id, space_id, role_id, allow_mask, 0
                FROM inserted_mount CROSS JOIN (VALUES (1, 16384), (2, 24576)) AS grants(role_id, allow_mask)
            )
            INSERT INTO mount_user_grants (mount_id, user_id, allow_mask, deny_mask)
            SELECT id, user_id, allow_mask, 0
            FROM inserted_mount CROSS JOIN (VALUES (1, 8192), (2, 24576)) AS grants(user_id, allow_mask);
            """, new { PublicId = publicId, SpaceId, SeededVersion, CreatedAt });
        return publicId;
    }

    private static async Task<Mount> ReadSnapshotAsync(WriterScope scope, Guid publicId)
    {
        var result = await scope.Services.GetRequiredService<IMountReader>().ReadAsync(SpaceId, publicId);
        AssertSuccess(result);
        Assert.That(result.Value, Is.Not.Null);
        return result.Value!;
    }

    private static Task<MountEntity?> FindMountAsync(
        AppDbContext context, Guid publicId, CancellationToken cancellationToken = default) =>
        context.Mounts.AsNoTracking().SingleOrDefaultAsync(mount => mount.PublicId == publicId, cancellationToken);

    private static Task AssertSeededGrantsAsync(
        AppDbContext context, long mountId, CancellationToken cancellationToken = default) =>
        AssertGrantsAsync(
            context,
            mountId,
            [(1, View, 0L), (2, ViewManage, 0L)],
            [(1, Manage, 0L), (2, ViewManage, 0L)],
            cancellationToken);

    private static async Task AssertGrantsAsync(
        AppDbContext context,
        long mountId,
        (long TargetId, long Allow, long Deny)[] expectedRoles,
        (long TargetId, long Allow, long Deny)[] expectedUsers,
        CancellationToken cancellationToken = default)
    {
        var roles = await context.MountRoleGrants.AsNoTracking()
            .Where(grant => grant.MountId == mountId).ToArrayAsync(cancellationToken);
        var users = await context.MountUserGrants.AsNoTracking()
            .Where(grant => grant.MountId == mountId).ToArrayAsync(cancellationToken);
        Assert.Multiple(() =>
        {
            Assert.That(roles.Select(grant => grant.SpaceId), Is.All.EqualTo(SpaceId));
            Assert.That(roles.Select(grant => (grant.RoleId, grant.AllowMask, grant.DenyMask)),
                Is.EquivalentTo(expectedRoles));
            Assert.That(users.Select(grant => (grant.UserId, grant.AllowMask, grant.DenyMask)),
                Is.EquivalentTo(expectedUsers));
        });
    }

    private static void AssertPreconditionFailed(ResultBase result)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && Equals(code, ErrorCodes.Common.PreconditionFailed)), Is.True, result.ToString());
    }

    private static void AssertSuccess(ResultBase result) => Assert.That(result.IsSuccess, Is.True, result.ToString());

    private WriterScope CreateScope() => new(services!.CreateAsyncScope());

    private sealed class WriterScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public AppDbContext Context => Services.GetRequiredService<AppDbContext>();
        public IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();
        public ITransactionExecutor Executor => Services.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private const string SeedGrantTargetsSql = """
        INSERT INTO users (id, public_id, email, normalized_email, user_name, normalized_user_name, display_name,
            password_hash, system_role, status, security_version, version, created_at, updated_at)
        SELECT id, gen_random_uuid(), 'user' || id || '@example.com', 'USER' || id || '@EXAMPLE.COM',
            'user' || id, 'USER' || id, 'User ' || id, 'stored-password-hash', 'USER', 'ACTIVE', 1, 1,
            @CreatedAt, @CreatedAt
        FROM generate_series(1, 3) AS targets(id);

        INSERT INTO spaces (id, public_id, owner_user_id, name, status, storage_used_bytes,
            storage_reserved_bytes, version, created_at, updated_at)
        VALUES
            (@SpaceId, gen_random_uuid(), 1, 'Primary space', 'ACTIVE', 0, 0, 1, @CreatedAt, @CreatedAt),
            (@OtherSpaceId, gen_random_uuid(), 1, 'Other space', 'ACTIVE', 0, 0, 1, @CreatedAt, @CreatedAt);

        INSERT INTO space_roles (id, public_id, space_id, name, normalized_name, position, is_managed,
            permission_mask, version, created_at, updated_at)
        SELECT id, gen_random_uuid(), CASE WHEN id = @ForeignRoleId THEN @OtherSpaceId ELSE @SpaceId END,
            'Role ' || id, 'ROLE ' || id, id, false, 1, 1, @CreatedAt, @CreatedAt
        FROM generate_series(1, @ForeignRoleId) AS targets(id);

        INSERT INTO space_memberships (public_id, space_id, user_id, status, version, joined_at, updated_at)
        SELECT gen_random_uuid(), @SpaceId, id, 'ACTIVE', 1, @CreatedAt, @CreatedAt
        FROM generate_series(2, 3) AS targets(id);
        """;
}
