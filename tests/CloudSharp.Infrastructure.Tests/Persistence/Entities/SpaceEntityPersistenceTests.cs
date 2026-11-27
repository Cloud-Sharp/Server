using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Entities;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SpaceEntityPersistenceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 6, 12, 34, 56, TimeSpan.Zero);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_space_mapping_tests")
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

        await using var scope = services.CreateAsyncScope();
        await GetContext(scope).Database.EnsureCreatedAsync();
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
    public async Task SaveChangesAsync_ShouldStoreRoleMaskAndPropagateGeneratedMembershipId()
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, memberId) = await SaveUsersAsync(context);
        var space = CreateSpace(ownerId);
        context.Spaces.Add(space);
        await context.SaveChangesAsync();

        var existingRole = CreateRole(space.Id, SpaceRoleSystemKey.Member);
        context.SpaceRoles.Add(existingRole);
        await context.SaveChangesAsync();

        var role = CreateRole(space.Id, SpaceRoleSystemKey.Viewer);
        var membership = CreateMembership(space.Id, memberId, SpaceMembershipStatus.Active);
        var assignment = MembershipRoleAssignment.Create(existingRole.Id, ownerId, Timestamp);
        context.SpaceRoles.Add(role);
        context.SpaceMembershipRoles.Add(SpaceMembershipRoleEntity.FromDomain(membership, assignment));

        await context.SaveChangesAsync();

        await using var readScope = services!.CreateAsyncScope();
        var readContext = GetContext(readScope);
        var savedRole = await readContext.SpaceRoles.AsNoTracking().SingleAsync(row => row.PublicId == role.PublicId);
        var savedMembership = await readContext.SpaceMemberships.AsNoTracking()
            .SingleAsync(row => row.PublicId == membership.PublicId);
        var membershipRole = await readContext.SpaceMembershipRoles.AsNoTracking()
            .SingleAsync(row => row.MembershipId == savedMembership.Id);
        var systemKey = await readContext.Database.SqlQuery<string>(
            $"""SELECT system_key AS "Value" FROM space_roles WHERE id = {savedRole.Id}""").SingleAsync();
        var storedMask = await readContext.Database.SqlQuery<long>(
            $"""SELECT permission_mask AS "Value" FROM space_roles WHERE id = {savedRole.Id}""").SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(savedRole.Id, Is.GreaterThan(0).And.EqualTo(role.Id));
            Assert.That(savedRole.SystemKey, Is.EqualTo(SpaceRoleSystemKey.Viewer));
            Assert.That(systemKey, Is.EqualTo("VIEWER"));
            Assert.That(storedMask, Is.EqualTo(16405L));
            Assert.That(savedRole.PermissionMask, Is.EqualTo(storedMask));
            Assert.That(savedRole.ToDomain().StoredPermissions, Is.EquivalentTo(new[]
            {
                SpacePermission.SpaceView,
                SpacePermission.FilesRead,
                SpacePermission.FilesDownload,
                SpacePermission.MountView,
            }));
            Assert.That(savedMembership.Id, Is.GreaterThan(0).And.EqualTo(membership.Id));
            Assert.That(membershipRole.RoleId, Is.EqualTo(existingRole.Id));
            Assert.That(membershipRole.SpaceId, Is.EqualTo(space.Id));
            Assert.That(membershipRole.AssignedByUserId, Is.EqualTo(ownerId));
            Assert.That(membershipRole.AssignedAt, Is.EqualTo(Timestamp));
        });
    }

    [TestCase(-1L)]
    [TestCase(32768L)]
    public async Task SaveChangesAsync_ShouldRejectInvalidRolePermissionMask(long mask)
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, _) = await SaveUsersAsync(context);
        var space = CreateSpace(ownerId);
        context.Spaces.Add(space);
        await context.SaveChangesAsync();

        var role = CreateRole(space.Id, SpaceRoleSystemKey.Viewer);
        role.PermissionMask = mask;
        context.SpaceRoles.Add(role);

        AssertPostgresViolation(context, PostgresErrorCodes.CheckViolation, "chk_space_roles_permission_mask");
    }

    [TestCase(false, "fk_space_membership_roles_role")]
    [TestCase(true, "fk_space_membership_roles_membership")]
    public async Task SaveChangesAsync_ShouldRejectCrossSpaceRoleAssignment(
        bool useRoleSpaceId,
        string constraintName)
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, memberId) = await SaveUsersAsync(context);
        var membershipSpace = CreateSpace(ownerId);
        var roleSpace = CreateSpace(ownerId);
        context.Spaces.AddRange(membershipSpace, roleSpace);
        await context.SaveChangesAsync();

        var membership = CreateMembership(membershipSpace.Id, memberId, SpaceMembershipStatus.Suspended);
        var role = CreateRole(roleSpace.Id, SpaceRoleSystemKey.Viewer);
        context.AddRange(membership, role);
        await context.SaveChangesAsync();

        context.SpaceMembershipRoles.Add(new SpaceMembershipRoleEntity
        {
            MembershipId = membership.Id,
            RoleId = role.Id,
            SpaceId = useRoleSpaceId ? roleSpace.Id : membershipSpace.Id,
            AssignedByUserId = ownerId,
            AssignedAt = Timestamp,
        });

        AssertPostgresViolation(context, PostgresErrorCodes.ForeignKeyViolation, constraintName);
    }

    [TestCase(SpaceMembershipStatus.Active, SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceMembershipStatus.Suspended, SpaceMembershipStatus.Active)]
    public async Task SaveChangesAsync_ShouldAllowLeftHistoryAndRejectDuplicateCurrentMembership(
        SpaceMembershipStatus existingStatus,
        SpaceMembershipStatus conflictingStatus)
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, memberId) = await SaveUsersAsync(context);
        var space = CreateSpace(ownerId);
        context.Spaces.Add(space);
        await context.SaveChangesAsync();

        context.SpaceMemberships.AddRange(
            CreateMembership(space.Id, memberId, SpaceMembershipStatus.Left),
            CreateMembership(space.Id, memberId, SpaceMembershipStatus.Left),
            CreateMembership(space.Id, memberId, existingStatus));
        await context.SaveChangesAsync();

        var historyCount = await context.SpaceMemberships.CountAsync(membership =>
            membership.SpaceId == space.Id && membership.UserId == memberId &&
            membership.Status == SpaceMembershipStatus.Left);
        Assert.That(historyCount, Is.EqualTo(2));

        context.SpaceMemberships.Add(CreateMembership(space.Id, memberId, conflictingStatus));
        AssertPostgresViolation(context, PostgresErrorCodes.UniqueViolation, "ux_space_memberships_current_space_user");
    }

    [Test]
    public async Task SaveChangesAsync_ShouldRejectDeletedSpaceWithoutDeletionStart()
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, _) = await SaveUsersAsync(context);
        var space = CreateSpace(ownerId);
        space.Status = SpaceStatus.Deleted;
        space.DeletedAt = Timestamp;
        context.Spaces.Add(space);

        AssertPostgresViolation(context, PostgresErrorCodes.CheckViolation, "chk_spaces_status_timestamps");
    }

    [Test]
    public async Task SaveChangesAsync_ShouldRejectStorageCountersOutsideLongRange()
    {
        await using var scope = services!.CreateAsyncScope();
        var context = GetContext(scope);
        var (ownerId, _) = await SaveUsersAsync(context);
        var space = CreateSpace(ownerId);
        space.UsedBytes = long.MaxValue;
        space.ReservedBytes = 1;
        context.Spaces.Add(space);

        AssertPostgresViolation(context, PostgresErrorCodes.CheckViolation, "chk_spaces_quota_capacity");
    }

    [Test]
    public async Task SaveChangesAsync_ShouldRejectStaleInviteVersion()
    {
        Guid publicId;
        await using (var scope = services!.CreateAsyncScope())
        {
            var context = GetContext(scope);
            var (ownerId, _) = await SaveUsersAsync(context);
            var space = CreateSpace(ownerId);
            context.Spaces.Add(space);
            await context.SaveChangesAsync();

            var invite = SpaceInvite.Create(
                space.Id,
                ownerId,
                TokenHash.Reconstitute($"hash-{Guid.NewGuid():N}"),
                Timestamp.AddDays(1),
                maxUses: null,
                now: Timestamp).Value;
            publicId = invite.PublicId;
            context.SpaceInvites.Add(SpaceInviteEntity.FromDomain(invite));
            await context.SaveChangesAsync();
        }

        await using var firstScope = services!.CreateAsyncScope();
        await using var secondScope = services!.CreateAsyncScope();
        var firstContext = GetContext(firstScope);
        var secondContext = GetContext(secondScope);
        var first = await firstContext.SpaceInvites.SingleAsync(invite => invite.PublicId == publicId);
        var second = await secondContext.SpaceInvites.SingleAsync(invite => invite.PublicId == publicId);

        var firstSnapshot = first.ToDomain();
        var revokedAt = Timestamp.AddMinutes(1);
        Assert.That(firstSnapshot.Revoke(revokedAt).IsSuccess, Is.True);
        first.UpdateFrom(firstSnapshot);
        await firstContext.SaveChangesAsync();

        var secondSnapshot = second.ToDomain();
        Assert.That(secondSnapshot.RecordAcceptance(Timestamp.AddMinutes(2)).IsSuccess, Is.True);
        second.UpdateFrom(secondSnapshot);
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await secondContext.SaveChangesAsync());

        await using var verifyScope = services!.CreateAsyncScope();
        var saved = await GetContext(verifyScope).SpaceInvites.AsNoTracking()
            .SingleAsync(invite => invite.PublicId == publicId);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Version, Is.EqualTo(2));
            Assert.That(saved.Status, Is.EqualTo(SpaceInviteStatus.Revoked));
            Assert.That(saved.RevokedAt, Is.EqualTo(revokedAt));
            Assert.That(saved.UseCount, Is.Zero);
        });
    }

    private static AppDbContext GetContext(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static void AssertPostgresViolation(AppDbContext context, string sqlState, string constraintName)
    {
        var exception = Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
        Assert.That(exception!.InnerException, Is.TypeOf<PostgresException>());
        var violation = (PostgresException)exception.InnerException!;
        Assert.Multiple(() =>
        {
            Assert.That(violation.SqlState, Is.EqualTo(sqlState));
            Assert.That(violation.ConstraintName, Is.EqualTo(constraintName));
        });
    }

    private static async Task<(long OwnerId, long MemberId)> SaveUsersAsync(AppDbContext context)
    {
        var owner = CreateUser();
        var member = CreateUser();
        context.Users.AddRange(owner, member);
        await context.SaveChangesAsync();
        return (owner.Id, member.Id);
    }

    private static UserEntity CreateUser()
    {
        var email = $"space-test-{Guid.NewGuid():N}@example.com";
        return new UserEntity
        {
            PublicId = Guid.CreateVersion7(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "pbkdf2-sha256$space-test",
            SystemRole = SystemRole.User,
            Status = UserStatus.Active,
            SecurityVersion = 1,
            Version = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };
    }

    private static SpaceEntity CreateSpace(long ownerId) => new()
    {
        PublicId = Guid.CreateVersion7(),
        OwnerUserId = ownerId,
        Name = "Test Space",
        Status = SpaceStatus.Active,
        Version = 1,
        CreatedAt = Timestamp,
        UpdatedAt = Timestamp,
    };

    private static SpaceRoleEntity CreateRole(long spaceId, SpaceRoleSystemKey systemKey) =>
        SpaceRoleEntity.FromDomain(SpaceRole.CreateManaged(spaceId, systemKey, Timestamp).Value);

    private static SpaceMembershipEntity CreateMembership(long spaceId, long userId, SpaceMembershipStatus status) => new()
    {
        PublicId = Guid.CreateVersion7(),
        SpaceId = spaceId,
        UserId = userId,
        Status = status,
        Version = 1,
        JoinedAt = Timestamp,
        UpdatedAt = Timestamp,
        SuspendedAt = status == SpaceMembershipStatus.Suspended ? Timestamp : null,
        LeftAt = status == SpaceMembershipStatus.Left ? Timestamp : null,
    };
}
