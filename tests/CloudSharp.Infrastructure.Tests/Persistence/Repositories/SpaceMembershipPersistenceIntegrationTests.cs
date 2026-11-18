using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;
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
public sealed class SpaceMembershipPersistenceIntegrationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_space_membership_tests")
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
    public async Task ReadPageAsync_ShouldExcludeOwnerBeforePaginationAndScopeToRequestedSpace()
    {
        var owner = await SeedUserAsync("Owner");
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var otherOwner = await SeedUserAsync("Other Owner");
        var otherSpace = await SeedSpaceAsync(otherOwner.Id, Timestamp);

        var activeMember = await SeedUserAsync("Active Member");
        var suspendedMember = await SeedUserAsync("Suspended Member");
        var leftMember = await SeedUserAsync("Left Member");
        var otherSpaceMember = await SeedUserAsync("Other Space Member");

        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var otherSpaceRole = await SeedManagedRoleAsync(otherSpace.Id, SpaceRoleSystemKey.Member, Timestamp);

        var activeMembership = await SeedMembershipAsync(
            space.Id, activeMember.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        var suspendedMembership = await SeedMembershipAsync(
            space.Id, suspendedMember.Id, SpaceMembershipStatus.Suspended, Timestamp.AddHours(2), Timestamp.AddHours(3));
        var leftMembership = await SeedMembershipAsync(
            space.Id, leftMember.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(3), Timestamp.AddHours(4));
        var ownerMembership = await SeedMembershipAsync(
            space.Id, owner.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(4), Timestamp.AddHours(4));
        var otherMembership = await SeedMembershipAsync(
            otherSpace.Id, otherSpaceMember.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(5), Timestamp.AddHours(5));

        await LinkAsync(activeMembership, memberRole, owner.Id, Timestamp.AddHours(1));
        await LinkAsync(activeMembership, viewerRole, owner.Id, Timestamp.AddHours(1));
        await LinkAsync(suspendedMembership, memberRole, owner.Id, Timestamp.AddHours(2));
        await LinkAsync(leftMembership, memberRole, owner.Id, Timestamp.AddHours(3));
        await LinkAsync(ownerMembership, memberRole, owner.Id, ownerMembership.JoinedAt);
        await LinkAsync(otherMembership, otherSpaceRole, otherOwner.Id, Timestamp.AddHours(5));

        await using var scope = CreateScope();
        var items = await ReadAllPagesAsync(scope, space.Id, owner.Id, limit: 2, sort: null, descending: true);

        Assert.That(items.Select(item => item.Membership.PublicId),
            Is.EqualTo(new[]
            {
                leftMembership.PublicId,
                suspendedMembership.PublicId,
                activeMembership.PublicId,
            }));
        var activeSnapshot = items.Single(item => item.Membership.PublicId == activeMembership.PublicId);
        var suspendedSnapshot = items.Single(item => item.Membership.PublicId == suspendedMembership.PublicId);
        var leftSnapshot = items.Single(item => item.Membership.PublicId == leftMembership.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.UserPublicId),
                Is.EquivalentTo(new[] { activeMember.PublicId, suspendedMember.PublicId, leftMember.PublicId }));
            Assert.That(activeSnapshot.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(suspendedSnapshot.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));
            Assert.That(leftSnapshot.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(leftSnapshot.Membership.LeftAt, Is.EqualTo(Timestamp.AddHours(4)));
            Assert.That(activeSnapshot.DisplayName, Is.EqualTo("Active Member"));
            Assert.That(activeSnapshot.UserName, Is.Null);
            Assert.That(activeSnapshot.Roles.Select(role => role.Id),
                Is.EquivalentTo(new[] { memberRole.Id, viewerRole.Id }));
            Assert.That(suspendedSnapshot.Roles.Select(role => role.Id), Is.EqualTo(new[] { memberRole.Id }));
            Assert.That(leftSnapshot.Roles.Select(role => role.Id), Is.EqualTo(new[] { memberRole.Id }));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ReadPageAsync_WithEqualSortValues_ShouldTieBreakByPublicIdAcrossPages(bool descending)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var joinedAt = Timestamp.AddHours(1);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var seeded = new List<SpaceMembershipEntity>();
        for (var index = 0; index < 5; index++)
        {
            var member = await SeedUserAsync($"Member {index}");
            var membership = await SeedMembershipAsync(
                space.Id, member.Id, SpaceMembershipStatus.Active, joinedAt, joinedAt);
            await LinkAsync(membership, memberRole, owner.Id, joinedAt);
            seeded.Add(membership);
        }

        await using var scope = CreateScope();
        var items = await ReadAllPagesAsync(scope, space.Id, owner.Id, limit: 2, sort: null, descending);

        var expected = descending
            ? seeded.OrderByDescending(membership => membership.PublicId).Select(membership => membership.PublicId)
            : seeded.OrderBy(membership => membership.PublicId).Select(membership => membership.PublicId);
        Assert.That(items.Select(item => item.Membership.PublicId), Is.EqualTo(expected));
    }

    [TestCase("joinedAt", true)]
    [TestCase("joinedAt", false)]
    [TestCase("updatedAt", true)]
    [TestCase("updatedAt", false)]
    public async Task ReadPageAsync_ShouldSortByRequestedFieldAndDirection(string sort, bool descending)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var earliestJoined = await SeedMembershipAsync(
            space.Id, (await SeedUserAsync("A")).Id, SpaceMembershipStatus.Active,
            Timestamp.AddHours(1), Timestamp.AddHours(5));
        var middleJoined = await SeedMembershipAsync(
            space.Id, (await SeedUserAsync("B")).Id, SpaceMembershipStatus.Active,
            Timestamp.AddHours(2), Timestamp.AddHours(4));
        var latestJoined = await SeedMembershipAsync(
            space.Id, (await SeedUserAsync("C")).Id, SpaceMembershipStatus.Active,
            Timestamp.AddHours(3), Timestamp.AddHours(3));
        foreach (var membership in new[] { earliestJoined, middleJoined, latestJoined })
        {
            await LinkAsync(membership, memberRole, owner.Id, membership.JoinedAt);
        }

        await using var scope = CreateScope();
        var items = await ReadAllPagesAsync(scope, space.Id, owner.Id, limit: 2, sort, descending);

        var expected = sort == "joinedAt"
            ? descending
                ? new[] { latestJoined.PublicId, middleJoined.PublicId, earliestJoined.PublicId }
                : new[] { earliestJoined.PublicId, middleJoined.PublicId, latestJoined.PublicId }
            : descending
                ? new[] { earliestJoined.PublicId, middleJoined.PublicId, latestJoined.PublicId }
                : new[] { latestJoined.PublicId, middleJoined.PublicId, earliestJoined.PublicId };
        Assert.That(items.Select(item => item.Membership.PublicId), Is.EqualTo(expected));
    }

    [TestCase("name")]
    [TestCase("createdAt")]
    [TestCase("joined_at")]
    public async Task ReadPageAsync_WithUnsupportedSortField_ShouldReturnSortInvalid(string sort)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        await using var scope = CreateScope();

        var result = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, null, 10, sort, descending: true);

        AssertError(result, ErrorCodes.Space.ListSortInvalid);
    }

    [Test]
    public async Task ReadPageAsync_WithTamperedCursor_ShouldReturnCursorInvalid()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        for (var index = 1; index <= 3; index++)
        {
            var member = await SeedUserAsync($"Member {index}");
            var membership = await SeedMembershipAsync(
                space.Id, member.Id, SpaceMembershipStatus.Active,
                Timestamp.AddHours(index), Timestamp.AddHours(index));
            await LinkAsync(membership, memberRole, owner.Id, membership.JoinedAt);
        }

        await using var scope = CreateScope();
        var first = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, null, 1, null, descending: true);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        Assert.That(first.Value.NextCursor, Is.Not.Null.And.Not.Empty);
        var cursor = first.Value.NextCursor!;
        var last = cursor[^1] == 'A' ? 'B' : 'A';
        var tampered = cursor[..^1] + last;

        var result = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, tampered, 1, null, descending: true);

        AssertError(result, ErrorCodes.Space.ListCursorInvalid);
    }

    [Test]
    public async Task ReadPageAsync_WithMismatchedCursorContext_ShouldReturnCursorInvalid()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var otherSpace = await SeedSpaceAsync(owner.Id, Timestamp);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var member = await SeedUserAsync();
        var firstMembership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        var secondMembership = await SeedMembershipAsync(
            space.Id, (await SeedUserAsync()).Id, SpaceMembershipStatus.Active, Timestamp.AddHours(2), Timestamp.AddHours(2));
        await LinkAsync(firstMembership, memberRole, owner.Id, Timestamp.AddHours(1));
        await LinkAsync(secondMembership, memberRole, owner.Id, Timestamp.AddHours(2));

        await using var scope = CreateScope();
        var first = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, null, 1, null, descending: true);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        var cursor = first.Value.NextCursor;
        Assert.That(cursor, Is.Not.Null.And.Not.Empty);

        var directionMismatch = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, cursor, 1, null, descending: false);
        var sortMismatch = await scope.MembershipReader.ReadPageAsync(
            space.Id, owner.Id, cursor, 1, "updatedAt", descending: true);
        var spaceMismatch = await scope.MembershipReader.ReadPageAsync(
            otherSpace.Id, owner.Id, cursor, 1, null, descending: true);
        var ownerMismatch = await scope.MembershipReader.ReadPageAsync(
            space.Id, member.Id, cursor, 1, null, descending: true);

        foreach (var result in new[] { directionMismatch, sortMismatch, spaceMismatch, ownerMismatch })
        {
            AssertError(result, ErrorCodes.Space.ListCursorInvalid);
        }
    }

    [Test]
    public async Task ReadAsync_ShouldRestoreLeftMembershipWithRolesAndRejectOtherScopes()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var otherSpace = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync("Left Member");
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var leftMembership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(1), Timestamp.AddHours(2));
        await LinkAsync(leftMembership, memberRole, owner.Id, Timestamp.AddHours(1));
        await LinkAsync(leftMembership, viewerRole, owner.Id, Timestamp.AddHours(2));

        await using var scope = CreateScope();
        var found = await scope.MembershipReader.ReadAsync(space.Id, leftMembership.PublicId);
        var crossSpace = await scope.MembershipReader.ReadAsync(otherSpace.Id, leftMembership.PublicId);
        var unknownPublicId = await scope.MembershipReader.ReadAsync(space.Id, Guid.CreateVersion7());

        Assert.That(found.IsSuccess, Is.True, found.ToString());
        Assert.That(found.Value, Is.Not.Null);
        var snapshot = found.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Membership.PublicId, Is.EqualTo(leftMembership.PublicId));
            Assert.That(snapshot.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(snapshot.Membership.LeftAt, Is.EqualTo(Timestamp.AddHours(2)));
            Assert.That(snapshot.Membership.RoleAssignments.Select(assignment => assignment.RoleId),
                Is.EquivalentTo(new[] { memberRole.Id, viewerRole.Id }));
            Assert.That(snapshot.UserPublicId, Is.EqualTo(member.PublicId));
            Assert.That(snapshot.DisplayName, Is.EqualTo("Left Member"));
            Assert.That(snapshot.Roles.Select(role => role.Id),
                Is.EquivalentTo(new[] { memberRole.Id, viewerRole.Id }));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
        Assert.Multiple(() =>
        {
            Assert.That(crossSpace.IsSuccess, Is.True, crossSpace.ToString());
            Assert.That(crossSpace.Value, Is.Null);
            Assert.That(unknownPublicId.IsSuccess, Is.True, unknownPublicId.ToString());
            Assert.That(unknownPublicId.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindByPublicIdAsync_ShouldReturnIndependentDetachedSnapshotsWithAssignments()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var membership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(2));
        await LinkAsync(membership, memberRole, owner.Id, Timestamp.AddHours(1));

        await using var scope = CreateScope();
        var first = await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        var second = await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        Assert.That(second.IsSuccess, Is.True, second.ToString());
        Assert.That(first.Value, Is.Not.Null);
        Assert.That(second.Value, Is.Not.Null);
        Assert.That(first.Value, Is.Not.SameAs(second.Value));

        var snapshot = first.Value!;
        var assignment = snapshot.RoleAssignments.Single();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Id, Is.EqualTo(membership.Id));
            Assert.That(snapshot.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(snapshot.Version, Is.EqualTo(1));
            Assert.That(assignment.RoleId, Is.EqualTo(memberRole.Id));
            Assert.That(assignment.AssignedByUserId, Is.EqualTo(owner.Id));
            Assert.That(assignment.AssignedAt, Is.EqualTo(Timestamp.AddHours(1)));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });

        var mutation = snapshot.Suspend(Timestamp.AddHours(3));
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());

        var reloaded = await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Value!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(reloaded.Value.Version, Is.EqualTo(1));
            Assert.That(second.Value!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
        });
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task FindLatestByUserIdAsync_ShouldPreferCurrentMembershipOverLeftHistory(
        SpaceMembershipStatus currentStatus)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var olderLeft = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp, Timestamp.AddHours(1));
        await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(1), Timestamp.AddHours(2));
        var current = await SeedMembershipAsync(
            space.Id, member.Id, currentStatus, Timestamp.AddHours(3), Timestamp.AddHours(3));
        await LinkAsync(olderLeft, memberRole, owner.Id, Timestamp);
        await LinkAsync(current, memberRole, owner.Id, Timestamp.AddHours(3));

        await using var scope = CreateScope();
        var result = await scope.MembershipRepository.FindLatestByUserIdAsync(space.Id, member.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.EqualTo(current.Id));
            Assert.That(result.Value.PublicId, Is.EqualTo(current.PublicId));
            Assert.That(result.Value.Status, Is.EqualTo(currentStatus));
            Assert.That(result.Value.RoleAssignments.Select(assignment => assignment.RoleId),
                Is.EqualTo(new[] { memberRole.Id }));
        });
    }

    [Test]
    public async Task FindLatestByUserIdAsync_WithOnlyLeftHistory_ShouldReturnMostRecentLeft()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var olderLeft = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp, Timestamp.AddHours(1));
        await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(1), Timestamp.AddHours(3));
        var newestLeftSameTime = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(2), Timestamp.AddHours(3));
        await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp.AddHours(1), Timestamp.AddHours(2));
        await LinkAsync(olderLeft, memberRole, owner.Id, Timestamp);

        await using var scope = CreateScope();
        var result = await scope.MembershipRepository.FindLatestByUserIdAsync(space.Id, member.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Id, Is.EqualTo(newestLeftSameTime.Id),
            "LEFT history must fall back to LeftAt descending, then internal id descending.");
    }

    [Test]
    public async Task FindLatestByUserIdAsync_WithUnknownUser_ShouldReturnNull()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        await using var scope = CreateScope();

        var result = await scope.MembershipRepository.FindLatestByUserIdAsync(space.Id, -1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.Value, Is.Null);
        });
    }

    [Test]
    public async Task AddAsync_WithoutSave_ShouldDeferPersistAndPropagateGeneratedIdToLinks()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var createdAt = Timestamp.AddHours(1);
        var creation = SpaceMembership.Create(
            space.Id, member.Id, [memberRole.ToDomain(), viewerRole.ToDomain()], owner.Id, createdAt);
        Assert.That(creation.IsSuccess, Is.True, creation.ToString());

        await using var scope = CreateScope();
        var add = await scope.MembershipRepository.AddAsync(creation.Value);
        Assert.Multiple(() =>
        {
            Assert.That(add.IsSuccess, Is.True, add.ToString());
            Assert.That(creation.Value.Id, Is.Zero);
        });
        var stagedMembership = scope.Context.SpaceMemberships.Local.Single();
        var stagedLinks = scope.Context.SpaceMembershipRoles.Local.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(scope.Context.Entry(stagedMembership).State, Is.EqualTo(EntityState.Added));
            Assert.That(stagedLinks, Has.Length.EqualTo(2));
            Assert.That(stagedLinks.Select(link => scope.Context.Entry(link).State),
                Is.All.EqualTo(EntityState.Added));
        });

        await using (var probe = CreateScope())
        {
            var beforeSave = await probe.MembershipRepository.FindByPublicIdAsync(space.Id, creation.Value.PublicId);
            Assert.Multiple(() =>
            {
                Assert.That(beforeSave.IsSuccess, Is.True, beforeSave.ToString());
                Assert.That(beforeSave.Value, Is.Null);
            });
        }

        var committed = await scope.Executor.ExecuteAsync(
            ct => scope.UnitOfWork.SaveChangesAsync(ct));
        Assert.That(committed.IsSuccess, Is.True, committed.ToString());

        await using (var verify = CreateScope())
        {
            var found = await verify.MembershipRepository.FindByPublicIdAsync(space.Id, creation.Value.PublicId);
            Assert.That(found.IsSuccess, Is.True, found.ToString());
            Assert.That(found.Value, Is.Not.Null);
            var links = await verify.Context.SpaceMembershipRoles.AsNoTracking()
                .Where(link => link.SpaceId == space.Id)
                .ToArrayAsync();
            Assert.Multiple(() =>
            {
                Assert.That(found.Value!.Id, Is.GreaterThan(0));
                Assert.That(found.Value.Status, Is.EqualTo(SpaceMembershipStatus.Active));
                Assert.That(found.Value.JoinedAt, Is.EqualTo(createdAt));
                Assert.That(found.Value.RoleAssignments.Select(assignment => assignment.RoleId),
                    Is.EquivalentTo(new[] { memberRole.Id, viewerRole.Id }));
                Assert.That(links, Has.Length.EqualTo(2));
                Assert.That(links.Select(link => link.MembershipId), Is.All.EqualTo(found.Value.Id));
                Assert.That(links.Select(link => link.AssignedByUserId), Is.All.EqualTo(owner.Id));
                Assert.That(links.Select(link => link.AssignedAt), Is.All.EqualTo(createdAt));
                Assert.That(creation.Value.Id, Is.Zero);
            });
        }
    }

    [Test]
    public async Task UpdateAsync_ShouldReconcileLinksAndPreserveRetainedAssignmentMetadata()
    {
        var owner = await SeedUserAsync();
        var admin = await SeedUserAsync("Admin");
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var editorRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Admin, Timestamp);
        var membership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(2));
        await LinkAsync(membership, memberRole, admin.Id, Timestamp.AddHours(1));
        await LinkAsync(membership, viewerRole, owner.Id, Timestamp.AddHours(2));

        await using var scope = CreateScope();
        var trackedMembership = await scope.Context.SpaceMemberships.SingleAsync(row => row.Id == membership.Id);
        var trackedAssignment = await scope.Context.SpaceMembershipRoles.SingleAsync(link =>
            link.MembershipId == membership.Id && link.RoleId == memberRole.Id);
        var found = await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        Assert.That(found.IsSuccess, Is.True, found.ToString());
        Assert.That(found.Value, Is.Not.Null);
        var expectedVersion = found.Value!.Version;
        var roles = await scope.RoleReader.ReadAsync(space.Id);
        Assert.That(roles.IsSuccess, Is.True, roles.ToString());
        var retainedRole = roles.Value.Single(role => role.Id == memberRole.Id);
        var addedRole = roles.Value.Single(role => role.Id == editorRole.Id);
        var changedAt = Timestamp.AddHours(3);
        var mutation = found.Value.ReplaceRoles([retainedRole, addedRole], owner.Id, changedAt);
        Assert.That(mutation.IsSuccess, Is.True, mutation.ToString());

        var update = await scope.MembershipRepository.UpdateAsync(found.Value, expectedVersion);
        Assert.That(update.IsSuccess, Is.True, update.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(scope.Context.SpaceMemberships.Local.Single(), Is.SameAs(trackedMembership));
            Assert.That(scope.Context.Entry(trackedAssignment).State, Is.EqualTo(EntityState.Unchanged));
        });
        var committed = await scope.Executor.ExecuteAsync(
            ct => scope.UnitOfWork.SaveChangesAsync(ct));
        Assert.That(committed.IsSuccess, Is.True, committed.ToString());

        await using (var verify = CreateScope())
        {
            var reloaded = await verify.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
            Assert.That(reloaded.IsSuccess, Is.True, reloaded.ToString());
            var persisted = reloaded.Value!;
            var assignments = persisted.RoleAssignments.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(persisted.Version, Is.EqualTo(expectedVersion + 1));
                Assert.That(persisted.UpdatedAt, Is.EqualTo(changedAt));
                Assert.That(assignments.Select(assignment => assignment.RoleId),
                    Is.EquivalentTo(new[] { memberRole.Id, editorRole.Id }));
                Assert.That(assignments.Single(assignment => assignment.RoleId == memberRole.Id).AssignedByUserId,
                    Is.EqualTo(admin.Id));
                Assert.That(assignments.Single(assignment => assignment.RoleId == memberRole.Id).AssignedAt,
                    Is.EqualTo(Timestamp.AddHours(1)));
                Assert.That(assignments.Single(assignment => assignment.RoleId == editorRole.Id).AssignedByUserId,
                    Is.EqualTo(owner.Id));
                Assert.That(assignments.Single(assignment => assignment.RoleId == editorRole.Id).AssignedAt,
                    Is.EqualTo(changedAt));
            });
            var links = await verify.Context.SpaceMembershipRoles.AsNoTracking()
                .Where(link => link.MembershipId == membership.Id)
                .ToArrayAsync();
            Assert.That(links, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task UpdateAsync_WhenRestagedBeforeSave_ShouldUseFinalRoleAssignments()
    {
        var owner = await SeedUserAsync();
        var member = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var initialRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var temporaryRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var joinedAt = Timestamp.AddHours(1);
        var membership = await SeedMembershipAsync(space.Id, member.Id, SpaceMembershipStatus.Active, joinedAt, joinedAt);
        await LinkAsync(membership, initialRole, owner.Id, joinedAt);

        await using var scope = CreateScope();
        var snapshot = (await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId)).Value!;
        var observedVersion = snapshot.Version;
        var restoredAt = joinedAt.AddMinutes(2);

        Assert.That(snapshot.ReplaceRoles([temporaryRole.ToDomain()], owner.Id, joinedAt.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That((await scope.MembershipRepository.UpdateAsync(snapshot, observedVersion)).IsSuccess, Is.True);
        Assert.That(snapshot.ReplaceRoles([initialRole.ToDomain()], member.Id, restoredAt).IsSuccess, Is.True);
        Assert.That((await scope.MembershipRepository.UpdateAsync(snapshot, observedVersion)).IsSuccess, Is.True);

        var save = await scope.UnitOfWork.SaveChangesAsync();
        Assert.That(save.IsSuccess, Is.True, save.ToString());

        await using var verify = CreateScope();
        var saved = (await verify.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId)).Value!;
        var role = saved.RoleAssignments.Single();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Version, Is.EqualTo(observedVersion + 2));
            Assert.That(role.RoleId, Is.EqualTo(initialRole.Id));
            Assert.That(role.AssignedByUserId, Is.EqualTo(member.Id));
            Assert.That(role.AssignedAt, Is.EqualTo(restoredAt));
        });
    }

    [Test]
    public async Task UpdateAsync_WithoutDomainChanges_ShouldStillCheckObservedVersion()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var membership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        await LinkAsync(membership, memberRole, owner.Id, Timestamp.AddHours(1));

        await using var scope = CreateScope();
        var found = await scope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        Assert.That(found.IsSuccess, Is.True, found.ToString());
        Assert.That(found.Value, Is.Not.Null);
        var expectedVersion = found.Value!.Version;
        await BumpMembershipVersionAsync(membership.Id);

        var update = await scope.MembershipRepository.UpdateAsync(found.Value, expectedVersion);
        Assert.That(update.IsSuccess, Is.True, update.ToString());

        var save = await scope.UnitOfWork.SaveChangesAsync();

        AssertError(save, ErrorCodes.Common.PreconditionFailed);
        await using (var verify = CreateScope())
        {
            var reloaded = await verify.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
            Assert.That(reloaded.IsSuccess, Is.True, reloaded.ToString());
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Value!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
                Assert.That(reloaded.Value.SuspendedAt, Is.Null);
                Assert.That(reloaded.Value.Version, Is.EqualTo(expectedVersion + 1));
                Assert.That(reloaded.Value.RoleAssignments.Select(assignment => assignment.RoleId),
                    Is.EqualTo(new[] { memberRole.Id }));
            });
        }
    }

    [Test]
    public async Task ReplaceRolesConcurrentlyAddingSameRole_LosingSave_ShouldFailWithPreconditionFailed()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var membership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        await LinkAsync(membership, memberRole, owner.Id, Timestamp.AddHours(1));

        await using var winningScope = CreateScope();
        await using var losingScope = CreateScope();
        var roles = await winningScope.RoleReader.ReadAsync(space.Id);
        Assert.That(roles.IsSuccess, Is.True, roles.ToString());
        var retainedRole = roles.Value.Single(role => role.Id == memberRole.Id);
        var addedRole = roles.Value.Single(role => role.Id == viewerRole.Id);

        var winning = await winningScope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        var losing = await losingScope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
        Assert.That(winning.IsSuccess, Is.True, winning.ToString());
        Assert.That(losing.IsSuccess, Is.True, losing.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(winning.Value!.Version, Is.EqualTo(1));
            Assert.That(losing.Value!.Version, Is.EqualTo(1));
        });

        var changedAt = Timestamp.AddHours(2);
        Assert.That(winning.Value!.ReplaceRoles([retainedRole, addedRole], owner.Id, changedAt).IsSuccess,
            Is.True);
        Assert.That(losing.Value!.ReplaceRoles([retainedRole, addedRole], owner.Id, changedAt).IsSuccess,
            Is.True);
        var winningUpdate = await winningScope.MembershipRepository.UpdateAsync(winning.Value, expectedVersion: 1);
        var losingUpdate = await losingScope.MembershipRepository.UpdateAsync(losing.Value, expectedVersion: 1);
        Assert.That(winningUpdate.IsSuccess, Is.True, winningUpdate.ToString());
        Assert.That(losingUpdate.IsSuccess, Is.True, losingUpdate.ToString());

        var winningSave = await winningScope.Executor.ExecuteAsync(
            ct => winningScope.UnitOfWork.SaveChangesAsync(ct));
        Assert.That(winningSave.IsSuccess, Is.True, winningSave.ToString());

        var losingSave = await losingScope.Executor.ExecuteAsync(
            ct => losingScope.UnitOfWork.SaveChangesAsync(ct));

        AssertError(losingSave, ErrorCodes.Common.PreconditionFailed);
        Assert.That(losingScope.Context.ChangeTracker.Entries(), Is.Empty);
        await using (var verify = CreateScope())
        {
            var reloaded = await verify.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId);
            Assert.That(reloaded.IsSuccess, Is.True, reloaded.ToString());
            var links = await verify.Context.SpaceMembershipRoles.AsNoTracking()
                .Where(link => link.MembershipId == membership.Id)
                .ToArrayAsync();
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Value!.Version, Is.EqualTo(2));
                Assert.That(reloaded.Value.RoleAssignments.Select(assignment => assignment.RoleId),
                    Is.EquivalentTo(new[] { memberRole.Id, viewerRole.Id }));
                Assert.That(links, Has.Length.EqualTo(2));
                Assert.That(links.Single(link => link.RoleId == memberRole.Id).AssignedAt,
                    Is.EqualTo(membership.JoinedAt));
                Assert.That(links.Single(link => link.RoleId == viewerRole.Id).AssignedAt, Is.EqualTo(changedAt));
            });
        }
    }

    [Test]
    public async Task UpdateAsync_WhenMembershipWasDeletedBeforeRoleInsert_ShouldReturnPreconditionFailed()
    {
        var owner = await SeedUserAsync();
        var member = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var initialRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var addedRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var joinedAt = Timestamp.AddHours(1);
        var membership = await SeedMembershipAsync(space.Id, member.Id, SpaceMembershipStatus.Active, joinedAt, joinedAt);
        await LinkAsync(membership, initialRole, owner.Id, joinedAt);

        await using var losingScope = CreateScope();
        var snapshot = (await losingScope.MembershipRepository.FindByPublicIdAsync(space.Id, membership.PublicId)).Value!;
        var observedVersion = snapshot.Version;
        var replacement = snapshot.ReplaceRoles([initialRole.ToDomain(), addedRole.ToDomain()], owner.Id, joinedAt.AddHours(1));
        Assert.That(replacement.IsSuccess, Is.True, replacement.ToString());
        var update = await losingScope.MembershipRepository.UpdateAsync(snapshot, observedVersion);
        Assert.That(update.IsSuccess, Is.True, update.ToString());

        await using (var winningScope = CreateScope())
        {
            var current = (await winningScope.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id)).Value!;
            var delete = await winningScope.OwnershipRepository.DeleteAsync(current, current.Version);
            Assert.That(delete.IsSuccess, Is.True, delete.ToString());
            var save = await winningScope.UnitOfWork.SaveChangesAsync();
            Assert.That(save.IsSuccess, Is.True, save.ToString());
        }

        var losingSave = await losingScope.Executor.ExecuteAsync(ct => losingScope.UnitOfWork.SaveChangesAsync(ct));

        AssertError(losingSave, ErrorCodes.Common.PreconditionFailed);
        Assert.That(losingScope.Context.ChangeTracker.Entries(), Is.Empty);
        await using var verify = CreateScope();
        Assert.That(await verify.Context.SpaceMembershipRoles.AnyAsync(link => link.MembershipId == membership.Id), Is.False);
    }

    [Test]
    public async Task WriteMethodsWithInvalidArguments_ShouldThrowBeforeStaging()
    {
        await using var scope = CreateScope();
        var role = SpaceRole.Reconstitute(
            5, Guid.CreateVersion7(), 1, "Role", "ROLE", null, null, 1, false, 1,
            Timestamp, Timestamp, null, [SpacePermission.SpaceView]);
        var membership = SpaceMembership.Create(1, 2, [role], 1, Timestamp).Value;
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.MembershipRepository.UpdateAsync(membership, expectedVersion: 1));
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.OwnershipRepository.DeleteAsync(membership, expectedVersion: 1));

        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.MembershipRepository.AddAsync(null!));
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.MembershipRepository.UpdateAsync(null!, expectedVersion: 1));
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.OwnershipRepository.DeleteAsync(null!, expectedVersion: 1));
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await scope.OwnershipRepository.AddAsync(null!));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.MembershipRepository.AddAsync(membership, cancelled.Token));
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.MembershipRepository.UpdateAsync(membership, 1, cancelled.Token));
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.OwnershipRepository.AddAsync(membership, cancelled.Token));
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await scope.OwnershipRepository.DeleteAsync(membership, 1, cancelled.Token));

        Assert.Multiple(() =>
        {
            Assert.That(scope.Context.SpaceMemberships.Local, Is.Empty);
            Assert.That(scope.Context.SpaceMembershipRoles.Local, Is.Empty);
        });
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task OwnershipFindByUserIdAsync_ShouldReturnCurrentMembershipWithAssignments(
        SpaceMembershipStatus currentStatus)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var leftHistory = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Left, Timestamp, Timestamp.AddHours(1));
        var current = await SeedMembershipAsync(
            space.Id, member.Id, currentStatus, Timestamp.AddHours(2), Timestamp.AddHours(3));
        await LinkAsync(leftHistory, memberRole, owner.Id, Timestamp);
        await LinkAsync(current, memberRole, owner.Id, Timestamp.AddHours(2));

        await using var scope = CreateScope();
        var result = await scope.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.That(result.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.EqualTo(current.Id));
            Assert.That(result.Value.PublicId, Is.EqualTo(current.PublicId));
            Assert.That(result.Value.Status, Is.EqualTo(currentStatus));
            Assert.That(result.Value.RoleAssignments.Select(assignment => assignment.RoleId),
                Is.EqualTo(new[] { memberRole.Id }));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public async Task OwnershipFindByUserIdAsync_WithoutCurrentMembership_ShouldReturnNull()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var otherSpace = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var onlyLeftUser = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var otherSpaceRole = await SeedManagedRoleAsync(otherSpace.Id, SpaceRoleSystemKey.Member, Timestamp);
        var otherSpaceMembership = await SeedMembershipAsync(
            otherSpace.Id, member.Id, SpaceMembershipStatus.Active, Timestamp, Timestamp);
        await LinkAsync(otherSpaceMembership, otherSpaceRole, owner.Id, Timestamp);
        var leftHistory = await SeedMembershipAsync(
            space.Id, onlyLeftUser.Id, SpaceMembershipStatus.Left, Timestamp, Timestamp);
        await LinkAsync(leftHistory, memberRole, owner.Id, Timestamp);

        await using var scope = CreateScope();
        var crossSpace = await scope.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id);
        var onlyLeft = await scope.OwnershipRepository.FindByUserIdAsync(space.Id, onlyLeftUser.Id);

        Assert.Multiple(() =>
        {
            Assert.That(crossSpace.IsSuccess, Is.True, crossSpace.ToString());
            Assert.That(crossSpace.Value, Is.Null);
            Assert.That(onlyLeft.IsSuccess, Is.True, onlyLeft.ToString());
            Assert.That(onlyLeft.Value, Is.Null);
        });
    }

    [Test]
    public async Task FindManagedAdminRoleAsync_ShouldReturnLiveManagedAdminScopedToSpace()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var otherSpace = await SeedSpaceAsync(owner.Id, Timestamp);
        var rolelessSpace = await SeedSpaceAsync(owner.Id, Timestamp);
        var deletedAdmin = await SeedManagedRoleAsync(
            space.Id, SpaceRoleSystemKey.Admin, Timestamp, deletedAt: Timestamp.AddHours(1));
        var liveAdmin = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Admin, Timestamp);
        var customAdmin = SpaceRoleEntity.FromDomain(SpaceRole.Reconstitute(
            0, Guid.CreateVersion7(), space.Id, "Custom Admin", "CUSTOM ADMIN", null, null, 2, false, 1,
            Timestamp, Timestamp, null, [SpacePermission.SpaceView]));
        await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var otherSpaceAdmin = await SeedManagedRoleAsync(otherSpace.Id, SpaceRoleSystemKey.Admin, Timestamp);
        await SeedManagedRoleAsync(rolelessSpace.Id, SpaceRoleSystemKey.Member, Timestamp);
        await using (var seedScope = CreateScope())
        {
            seedScope.Context.SpaceRoles.Add(customAdmin);
            await seedScope.Context.SaveChangesAsync();
        }

        await using var scope = CreateScope();
        var found = await scope.OwnershipRepository.FindManagedAdminRoleAsync(space.Id);
        var otherSpaceFound = await scope.OwnershipRepository.FindManagedAdminRoleAsync(otherSpace.Id);
        var roleless = await scope.OwnershipRepository.FindManagedAdminRoleAsync(rolelessSpace.Id);

        Assert.That(found.IsSuccess, Is.True, found.ToString());
        Assert.That(found.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(found.Value!.Id, Is.EqualTo(liveAdmin.Id));
            Assert.That(found.Value.SpaceId, Is.EqualTo(space.Id));
            Assert.That(found.Value.IsManaged, Is.True);
            Assert.That(found.Value.SystemKey, Is.EqualTo(SpaceRoleSystemKey.Admin));
            Assert.That(found.Value.DeletedAt, Is.Null);
            Assert.That(otherSpaceFound.Value!.Id, Is.EqualTo(otherSpaceAdmin.Id));
            Assert.That(roleless.IsSuccess, Is.True, roleless.ToString());
            Assert.That(roleless.Value, Is.Null);
        });
        Assert.That(deletedAdmin.Id, Is.LessThan(liveAdmin.Id),
            "The deleted admin must sort first so the query has to skip it.");
    }

    [Test]
    public async Task DeleteAsync_WithObservedVersion_ShouldDeleteMembershipAndCascadeLinks()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var member = await SeedUserAsync();
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var viewerRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Viewer, Timestamp);
        var membership = await SeedMembershipAsync(
            space.Id, member.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        await LinkAsync(membership, memberRole, owner.Id, Timestamp.AddHours(1));
        await LinkAsync(membership, viewerRole, owner.Id, Timestamp.AddHours(1));

        await using var scope = CreateScope();
        var found = await scope.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id);
        Assert.That(found.IsSuccess, Is.True, found.ToString());
        Assert.That(found.Value, Is.Not.Null);
        var observedVersion = found.Value!.Version;

        var delete = await scope.OwnershipRepository.DeleteAsync(found.Value, observedVersion);
        Assert.That(delete.IsSuccess, Is.True, delete.ToString());

        await using (var probe = CreateScope())
        {
            var beforeSave = await probe.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id);
            Assert.Multiple(() =>
            {
                Assert.That(beforeSave.IsSuccess, Is.True, beforeSave.ToString());
                Assert.That(beforeSave.Value, Is.Not.Null);
            });
        }

        var committed = await scope.Executor.ExecuteAsync(
            ct => scope.UnitOfWork.SaveChangesAsync(ct));
        Assert.That(committed.IsSuccess, Is.True, committed.ToString());

        await using (var verify = CreateScope())
        {
            var reloaded = await verify.OwnershipRepository.FindByUserIdAsync(space.Id, member.Id);
            var membershipCount = await verify.Context.SpaceMemberships.AsNoTracking()
                .CountAsync(candidate => candidate.Id == membership.Id);
            var linkCount = await verify.Context.SpaceMembershipRoles.AsNoTracking()
                .CountAsync(link => link.MembershipId == membership.Id);
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Value, Is.Null);
                Assert.That(membershipCount, Is.Zero);
                Assert.That(linkCount, Is.Zero);
            });
        }
    }

    [Test]
    public async Task OwnershipTransfer_ShouldSaveOwnerChangeAndMembershipChangesTogether()
    {
        var owner = await SeedUserAsync();
        var newOwner = await SeedUserAsync("New Owner");
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        var adminRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Admin, Timestamp);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var targetMembership = await SeedMembershipAsync(
            space.Id, newOwner.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        await LinkAsync(targetMembership, memberRole, owner.Id, targetMembership.JoinedAt);

        var changedAt = Timestamp.AddHours(2);
        await using var scope = CreateScope();
        var actor = CurrentActor.ForSession(owner.Id, owner.PublicId, owner.SystemRole, Guid.CreateVersion7());
        var authorization = SessionSpaceAuthorizationPolicy.Evaluate(
            new SpaceAuthorizationQuery(actor, space.PublicId, [], [SpaceStatus.Active]),
            new SpaceAuthorizationSnapshot(space, null, []));
        Assert.That(authorization.IsSuccess, Is.True, authorization.ToString());
        var useCase = new TransferSpaceOwnershipUseCase(
            scope.SpaceDetailsReader,
            scope.UserRepository,
            scope.SpaceRepository,
            scope.OwnershipRepository,
            scope.UnitOfWork,
            scope.Executor,
            new FakeClock(changedAt));

        var transfer = await useCase.ExecuteAsync(
            new TransferSpaceOwnershipCommand(authorization.Value, space.Version, newOwner.PublicId));

        Assert.That(transfer.IsSuccess, Is.True, transfer.ToString());

        await using var verify = CreateScope();
        var updatedSpace = (await verify.SpaceRepository.FindByIdAsync(space.Id)).Value!;
        var oldOwnerMembership = (await verify.OwnershipRepository.FindByUserIdAsync(space.Id, owner.Id)).Value!;
        var newOwnerMembership = (await verify.OwnershipRepository.FindByUserIdAsync(space.Id, newOwner.Id)).Value;
        var remainingLinks = await verify.Context.SpaceMembershipRoles.AsNoTracking()
            .Where(link => link.SpaceId == space.Id)
            .ToArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(updatedSpace.OwnerUserId, Is.EqualTo(newOwner.Id));
            Assert.That(updatedSpace.Version, Is.EqualTo(space.Version + 1));
            Assert.That(newOwnerMembership, Is.Null);
            Assert.That(oldOwnerMembership.Id, Is.GreaterThan(0));
            Assert.That(oldOwnerMembership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(oldOwnerMembership.JoinedAt, Is.EqualTo(changedAt));
            Assert.That(remainingLinks, Has.Length.EqualTo(1));
        });
        var adminLink = remainingLinks.Single();
        Assert.Multiple(() =>
        {
            Assert.That(adminLink.MembershipId, Is.EqualTo(oldOwnerMembership.Id));
            Assert.That(adminLink.RoleId, Is.EqualTo(adminRole.Id));
            Assert.That(adminLink.AssignedByUserId, Is.EqualTo(owner.Id));
        });
    }

    [Test]
    public async Task DeleteAsync_OnStaleMembershipVersion_ShouldRollbackSp10ShapedTransfer()
    {
        var owner = await SeedUserAsync();
        var newOwner = await SeedUserAsync("New Owner");
        var space = await SeedSpaceAsync(owner.Id, Timestamp);
        await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Admin, Timestamp);
        var memberRole = await SeedManagedRoleAsync(space.Id, SpaceRoleSystemKey.Member, Timestamp);
        var newOwnerMembership = await SeedMembershipAsync(
            space.Id, newOwner.Id, SpaceMembershipStatus.Active, Timestamp.AddHours(1), Timestamp.AddHours(1));
        await LinkAsync(newOwnerMembership, memberRole, owner.Id, Timestamp.AddHours(1));

        await using var scope = CreateScope();
        var spaceRead = await scope.SpaceRepository.FindByIdAsync(space.Id);
        var membershipRead = await scope.OwnershipRepository.FindByUserIdAsync(space.Id, newOwner.Id);
        var adminRoleRead = await scope.OwnershipRepository.FindManagedAdminRoleAsync(space.Id);
        Assert.That(spaceRead.IsSuccess, Is.True, spaceRead.ToString());
        Assert.That(membershipRead.IsSuccess, Is.True, membershipRead.ToString());
        Assert.That(adminRoleRead.IsSuccess, Is.True, adminRoleRead.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(spaceRead.Value, Is.Not.Null);
            Assert.That(membershipRead.Value, Is.Not.Null);
            Assert.That(adminRoleRead.Value, Is.Not.Null);
        });
        var observedSpaceVersion = spaceRead.Value!.Version;
        var observedMembershipVersion = membershipRead.Value!.Version;

        await BumpMembershipVersionAsync(newOwnerMembership.Id);

        var changedAt = Timestamp.AddHours(2);
        var transfer = spaceRead.Value.TransferOwnership(newOwner.Id, changedAt);
        Assert.That(transfer.IsSuccess, Is.True, transfer.ToString());
        var creation = SpaceMembership.Create(
            space.Id, owner.Id, [adminRoleRead.Value!], owner.Id, changedAt);
        Assert.That(creation.IsSuccess, Is.True, creation.ToString());

        var save = await scope.Executor.ExecuteAsync(async ct =>
        {
            var delete = await scope.OwnershipRepository.DeleteAsync(
                membershipRead.Value!, observedMembershipVersion, ct);
            var update = await scope.SpaceRepository.UpdateAsync(spaceRead.Value, observedSpaceVersion, ct);
            var add = await scope.OwnershipRepository.AddAsync(creation.Value, ct);
            Assert.That(delete.IsSuccess, Is.True, delete.ToString());
            Assert.That(update.IsSuccess, Is.True, update.ToString());
            Assert.That(add.IsSuccess, Is.True, add.ToString());
            return await scope.UnitOfWork.SaveChangesAsync(ct);
        });

        AssertError(save, ErrorCodes.Common.PreconditionFailed);
        Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        await using (var verify = CreateScope())
        {
            var persistedSpace = await verify.SpaceRepository.FindByIdAsync(space.Id);
            var persistedMembership = await verify.OwnershipRepository.FindByUserIdAsync(space.Id, newOwner.Id);
            var previousOwnerMembership = await verify.OwnershipRepository.FindByUserIdAsync(space.Id, owner.Id);
            var linkCount = await verify.Context.SpaceMembershipRoles.AsNoTracking()
                .CountAsync(link => link.MembershipId == newOwnerMembership.Id);
            Assert.That(persistedMembership.Value, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(persistedSpace.Value!.OwnerUserId, Is.EqualTo(owner.Id));
                Assert.That(persistedSpace.Value.Version, Is.EqualTo(observedSpaceVersion));
                Assert.That(persistedMembership.Value!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
                Assert.That(persistedMembership.Value.Version, Is.EqualTo(observedMembershipVersion + 1));
                Assert.That(previousOwnerMembership.Value, Is.Null);
                Assert.That(linkCount, Is.EqualTo(1));
            });
        }
    }

    private RepositoryScope CreateScope() => new(services!.CreateAsyncScope());

    private static async Task<List<SpaceMembershipReadSnapshot>> ReadAllPagesAsync(
        RepositoryScope scope,
        long spaceId,
        long ownerUserId,
        int limit,
        string? sort,
        bool descending)
    {
        var items = new List<SpaceMembershipReadSnapshot>();
        var traversedCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        while (true)
        {
            var result = await scope.MembershipReader.ReadPageAsync(
                spaceId, ownerUserId, cursor, limit, sort, descending);
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            var page = result.Value;
            Assert.That(page.Items.Count, Is.LessThanOrEqualTo(limit));
            items.AddRange(page.Items);
            if (page.NextCursor is null)
            {
                return items;
            }

            Assert.That(page.Items.Count, Is.EqualTo(limit),
                "A non-terminal page must be full.");
            Assert.That(traversedCursors.Add(page.NextCursor), Is.True,
                "Membership cursor did not advance.");
            cursor = page.NextCursor;
        }
    }

    private async Task<User> SeedUserAsync(string displayName = "Member")
    {
        await using var scope = CreateScope();
        var user = UserEntity.FromDomain(User.Create(
            $"member-{Guid.NewGuid():N}@example.com",
            null,
            displayName,
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value);
        scope.Context.Users.Add(user);
        await scope.Context.SaveChangesAsync();
        return user.ToDomain();
    }

    private async Task<Space> SeedSpaceAsync(long ownerId, DateTimeOffset createdAt)
    {
        await using var scope = CreateScope();
        var space = Space.Create(
            ownerId,
            $"Space {Guid.NewGuid():N}",
            null,
            4_096,
            createdAt).Value;
        scope.Context.Spaces.Add(SpaceEntity.FromDomain(space));
        await scope.Context.SaveChangesAsync();
        var persisted = await scope.Context.Spaces.AsNoTracking()
            .SingleAsync(entity => entity.PublicId == space.PublicId);
        return persisted.ToDomain();
    }

    private async Task<SpaceMembershipEntity> SeedMembershipAsync(
        long spaceId,
        long userId,
        SpaceMembershipStatus status,
        DateTimeOffset joinedAt,
        DateTimeOffset updatedAt)
    {
        await using var scope = CreateScope();
        var membership = new SpaceMembershipEntity
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            UserId = userId,
            Status = status,
            Version = 1,
            JoinedAt = joinedAt,
            UpdatedAt = updatedAt,
            SuspendedAt = status == SpaceMembershipStatus.Suspended ? updatedAt : null,
            LeftAt = status == SpaceMembershipStatus.Left ? updatedAt : null,
        };
        scope.Context.SpaceMemberships.Add(membership);
        await scope.Context.SaveChangesAsync();
        return membership;
    }

    private async Task<SpaceRoleEntity> SeedManagedRoleAsync(
        long spaceId,
        SpaceRoleSystemKey systemKey,
        DateTimeOffset createdAt,
        DateTimeOffset? deletedAt = null)
    {
        await using var scope = CreateScope();
        var role = SpaceRoleEntity.FromDomain(SpaceRole.CreateManaged(spaceId, systemKey, createdAt).Value);
        role.DeletedAt = deletedAt;
        role.UpdatedAt = deletedAt ?? createdAt;
        scope.Context.SpaceRoles.Add(role);
        await scope.Context.SaveChangesAsync();
        return role;
    }

    private async Task LinkAsync(
        SpaceMembershipEntity membership,
        SpaceRoleEntity role,
        long assignedByUserId,
        DateTimeOffset assignedAt)
    {
        await using var scope = CreateScope();
        scope.Context.SpaceMembershipRoles.Add(new SpaceMembershipRoleEntity
        {
            MembershipId = membership.Id,
            RoleId = role.Id,
            SpaceId = membership.SpaceId,
            AssignedByUserId = assignedByUserId,
            AssignedAt = assignedAt,
        });
        await scope.Context.SaveChangesAsync();
    }

    private async Task BumpMembershipVersionAsync(long membershipId)
    {
        await using var competitor = CreateScope();
        await competitor.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE space_memberships SET version = version + 1 WHERE id = {membershipId}");
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && Equals(code, expectedCode)), Is.True,
            $"Expected error code {expectedCode} in {result}.");
    }

    private sealed class RepositoryScope(AsyncServiceScope serviceScope) : IAsyncDisposable
    {
        public AppDbContext Context => serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();

        public ISpaceMembershipReader MembershipReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceMembershipReader>();

        public ISpaceMembershipRepository MembershipRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceMembershipRepository>();

        public ISpaceOwnershipMembershipRepository OwnershipRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceOwnershipMembershipRepository>();

        public ISpaceRepository SpaceRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceRepository>();

        public ISpaceDetailsReader SpaceDetailsReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceDetailsReader>();

        public IUserRepository UserRepository =>
            serviceScope.ServiceProvider.GetRequiredService<IUserRepository>();

        public ISpaceRoleReader RoleReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceRoleReader>();

        public IUnitOfWork UnitOfWork => serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public ITransactionExecutor Executor =>
            serviceScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => serviceScope.DisposeAsync();
    }
}
