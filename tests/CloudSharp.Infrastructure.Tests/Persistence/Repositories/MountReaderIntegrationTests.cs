using System.Diagnostics;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Dapper;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class MountReaderIntegrationTests
{
    private const long SpaceId = 100;
    private const long OtherSpaceId = 200;
    private const long OwnerId = 1;
    private const long MemberId = 2;
    private const long SuspendedId = 3;
    private const long RejoinedId = 4;
    private const long OtherMemberId = 6;
    private const long WriterRoleId = 2;
    private const long DeletedRoleId = 3;
    private const long AdminRoleId = 4;
    private const long FirstMountId = 101;
    private const long ArchivedMountId = 102;
    private const long FilelessMountId = 103;
    private const long CurrentMembershipId = 14;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 26, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = CreatedAt.AddDays(1);
    private static readonly Guid OwnerPublicId = new("019b26d0-0000-7000-8000-000000000090");
    private static readonly Guid MemberPublicId = new("019b26d0-0000-7000-8000-000000000010");
    private static readonly Guid SuspendedPublicId = new("019b26d0-0000-7000-8000-000000000050");
    private static readonly Guid RejoinedPublicId = new("019b26d0-0000-7000-8000-000000000030");
    private static readonly Guid OtherOwnerPublicId = new("019b26d0-0000-7000-8000-000000000070");
    private static readonly Guid OtherMemberPublicId = new("019b26d0-0000-7000-8000-000000000020");
    private static readonly Guid WriterRolePublicId = new("019b26d0-0000-7000-8000-0000000000c0");
    private static readonly Guid DeletedRolePublicId = new("019b26d0-0000-7000-8000-000000000040");
    private static readonly Guid AdminRolePublicId = new("019b26d0-0000-7000-8000-000000000080");
    private static readonly Guid ForeignRolePublicId = new("019b26d0-0000-7000-8000-000000000060");
    private static readonly Guid FirstMountPublicId = new("019b26d0-0000-7000-8000-000000000400");
    private static readonly Guid ArchivedMountPublicId = new("019b26d0-0000-7000-8000-000000000100");
    private static readonly Guid FilelessMountPublicId = new("019b26d0-0000-7000-8000-000000000300");
    private static readonly Guid LastMountPublicId = new("019b26d0-0000-7000-8000-000000000200");
    private static readonly Guid OtherMountPublicId = new("019b26d0-0000-7000-8000-000000001500");

    private PostgreSqlContainer? postgres;
    private NpgsqlConnection? sql;
    private ServiceProvider? services;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_mount_reader_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        sql = new NpgsqlConnection(postgres.GetConnectionString());
        await sql.OpenAsync();

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

    [SetUp]
    public async Task SetUp()
    {
        await sql!.ExecuteAsync("""
            TRUNCATE TABLE
                mount_user_grants,
                mount_role_grants,
                mounts,
                space_membership_roles,
                space_memberships,
                space_roles,
                space_invites,
                spaces,
                users
            RESTART IDENTITY CASCADE;
            """);
        await sql!.ExecuteAsync(SeedSql, new
        {
            CreatedAt,
            Later,
            PriorJoinedAt = CreatedAt.AddDays(-2),
            PriorLeftAt = CreatedAt.AddDays(-1),
            MemberUpdatedAt = CreatedAt.AddHours(2),
            SuspendedAt = CreatedAt.AddHours(3),
            RejoinedUpdatedAt = CreatedAt.AddHours(4),
            RoleDeletedAt = CreatedAt.AddHours(5),
            MountArchivedAt = CreatedAt.AddHours(6),
            LastMountArchivedAt = Later.AddHours(2),
            OwnerPublicId,
            MemberPublicId,
            SuspendedPublicId,
            RejoinedPublicId,
            OtherOwnerPublicId,
            OtherMemberPublicId,
            WriterRolePublicId,
            DeletedRolePublicId,
            AdminRolePublicId,
            ForeignRolePublicId,
            FirstMountPublicId,
            ArchivedMountPublicId,
            FilelessMountPublicId,
            LastMountPublicId,
            OtherMountPublicId,
        });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (sql is not null)
        {
            await sql.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task ListReadAsync_ShouldPaginateBothDirectionsWithPublicIdTieBreakersAndGrants()
    {
        await using var scope = CreateScope();
        var actor = CreateActor();

        var descending = await ReadAllMountsAsync(scope, actor, sort: null, descending: true, batchSize: 2);
        var ascending = await ReadAllMountsAsync(scope, actor, "CREATED_AT", descending: false, batchSize: 2);
        var expectedDescending = new[]
        {
            FilelessMountPublicId,
            LastMountPublicId,
            FirstMountPublicId,
            ArchivedMountPublicId,
        };

        Assert.Multiple(() =>
        {
            Assert.That(descending.Select(candidate => candidate.Mount.PublicId), Is.EqualTo(expectedDescending));
            Assert.That(ascending.Select(candidate => candidate.Mount.PublicId),
                Is.EqualTo(expectedDescending.Reverse()));
            Assert.That(descending.Select(candidate => candidate.Mount.Id), Is.Unique);
            Assert.That(descending.Select(candidate => candidate.Mount.SpaceId), Is.All.EqualTo(SpaceId));
        });

        var privateMount = descending.Single(candidate => candidate.Mount.Id == FirstMountId).Mount;
        var archivedMount = descending.Single(candidate => candidate.Mount.Id == ArchivedMountId).Mount;
        Assert.Multiple(() =>
        {
            Assert.That(privateMount.Visibility, Is.EqualTo(MountVisibility.Private));
            Assert.That(privateMount.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(RequireGrant(privateMount, MountGrantTargetType.Role, WriterRoleId).Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesWrite }));
            Assert.That(RequireGrant(privateMount, MountGrantTargetType.User, MemberId).Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesDownload }));
            Assert.That(archivedMount.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(archivedMount.Version, Is.EqualTo(7));
            Assert.That(RequireGrant(archivedMount, MountGrantTargetType.Role, DeletedRoleId).Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
            Assert.That(RequireGrant(archivedMount, MountGrantTargetType.User, SuspendedId).Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesWrite, MountPermission.FilesDownload }));
        });
    }

    [Test]
    public async Task ListReadAsync_ShouldResumeFromAnyCandidateCursorWithinBatch()
    {
        await using var scope = CreateScope();
        var actor = CreateActor();
        var first = RequireSuccess(await scope.ListCandidates.ReadAsync(
            SpaceId, actor, null, 3, "CREATED_AT", descending: false));
        Assert.Multiple(() =>
        {
            Assert.That(first.Candidates, Has.Count.EqualTo(3));
            Assert.That(first.HasMore, Is.True);
        });

        var resumed = RequireSuccess(await scope.ListCandidates.ReadAsync(
            SpaceId, actor, first.Candidates[1].CursorAfter, 3, "CREATED_AT", descending: false));

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Candidates.Select(candidate => candidate.Mount.PublicId),
                Is.EqualTo(new[] { LastMountPublicId, FilelessMountPublicId }));
            Assert.That(resumed.HasMore, Is.False);
        });
    }

    [Test]
    public async Task ListReadAsync_ShouldRejectTamperedOrReboundCursor()
    {
        await using var scope = CreateScope();
        var actor = CreateActor();
        var page = RequireSuccess(await scope.ListCandidates.ReadAsync(
            SpaceId, actor, null, 1, null, descending: true));
        var cursor = page.Candidates.Single().CursorAfter;
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        var otherActor = CurrentActor.ForSession(
            5, OtherOwnerPublicId, SystemRole.User, new Guid("019b26d0-0000-7000-8000-000000004002"));

        var invalid = new[]
        {
            await scope.ListCandidates.ReadAsync(SpaceId, actor, tampered, 1, null, true),
            await scope.ListCandidates.ReadAsync(OtherSpaceId, actor, cursor, 1, null, true),
            await scope.ListCandidates.ReadAsync(SpaceId, otherActor, cursor, 1, null, true),
            await scope.ListCandidates.ReadAsync(SpaceId, actor, cursor, 1, null, false),
        };

        foreach (var result in invalid)
        {
            AssertError(result, ErrorCodes.Common.CursorInvalid);
        }
    }

    [Test]
    public async Task CreationGrantTargetReadAsync_ShouldResolveScopedRolesAndAccountsWithLatestMembership()
    {
        await using var scope = CreateScope();
        var missingRole = new Guid("019b26d0-0000-7000-8000-000000005001");
        var missingUser = new Guid("019b26d0-0000-7000-8000-000000005002");

        var result = await scope.Executor.ExecuteAsync(cancellationToken =>
            scope.CreationTargets.ReadAsync(
                SpaceId,
                [WriterRolePublicId, DeletedRolePublicId, ForeignRolePublicId, missingRole],
                [OwnerPublicId, MemberPublicId, SuspendedPublicId, RejoinedPublicId, OtherMemberPublicId, missingUser],
                cancellationToken));
        var targets = RequireSuccess(result);

        Assert.Multiple(() =>
        {
            Assert.That(targets.Roles.Keys, Is.EquivalentTo(new[] { WriterRolePublicId, DeletedRolePublicId }));
            Assert.That(targets.Roles[WriterRolePublicId].SpaceId, Is.EqualTo(SpaceId));
            Assert.That(targets.Roles[DeletedRolePublicId].DeletedAt, Is.EqualTo(CreatedAt.AddHours(5)));
            Assert.That(targets.Users.Keys, Is.EquivalentTo(new[]
            {
                OwnerPublicId,
                MemberPublicId,
                SuspendedPublicId,
                RejoinedPublicId,
                OtherMemberPublicId,
            }));
            Assert.That(targets.Users[OwnerPublicId].User.Id, Is.EqualTo(OwnerId));
            Assert.That(targets.Users[OwnerPublicId].Membership, Is.Null);
            Assert.That(targets.Users[OtherMemberPublicId].User.Id, Is.EqualTo(OtherMemberId));
            Assert.That(targets.Users[OtherMemberPublicId].Membership, Is.Null);
            Assert.That(targets.Users[SuspendedPublicId].User.Status, Is.EqualTo(UserStatus.Suspended));
            Assert.That(targets.Users[SuspendedPublicId].Membership!.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));
            Assert.That(targets.Users[RejoinedPublicId].Membership!.Id, Is.EqualTo(CurrentMembershipId));
            Assert.That(targets.Users[RejoinedPublicId].Membership!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(targets.Users[MemberPublicId].Membership!.RoleAssignments, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task CreationGrantTargetReadAsync_ShouldWaitForMembershipCommitAndReturnLeft()
    {
        await using var writerScope = CreateScope();
        await using var readerScope = CreateScope();
        var writerLocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftAt = Later.AddHours(1);

        var writerTransaction = writerScope.Executor.ExecuteAsync(async cancellationToken =>
        {
            var found = await writerScope.Memberships.FindLatestByUserIdAsync(SpaceId, MemberId, cancellationToken);
            if (found.IsFailed)
            {
                return Result.Fail(found.Errors);
            }

            if (found.Value is null)
            {
                return Result.Fail("The seeded membership was not found.");
            }

            var membership = found.Value;
            var leave = membership.Leave(leftAt);
            if (leave.IsFailed)
            {
                return leave;
            }

            var update = await writerScope.Memberships.UpdateAsync(membership, 3, cancellationToken);
            if (update.IsFailed)
            {
                return update;
            }

            var save = await writerScope.UnitOfWork.SaveChangesAsync(cancellationToken);
            if (save.IsFailed)
            {
                return Result.Fail(save.Errors);
            }

            writerLocked.SetResult(await GetBackendPidAsync(writerScope.Context, cancellationToken));
            await commitBarrier.Task.WaitAsync(cancellationToken);
            return Result.Ok();
        });

        var writerSignal = await Task.WhenAny(writerLocked.Task, writerTransaction).WaitAsync(TestTimeout);
        if (writerSignal == writerTransaction)
        {
            var earlyWriterResult = await writerTransaction;
            throw new AssertionException($"Writer completed before the commit barrier: {earlyWriterResult}");
        }

        var writerPid = await writerLocked.Task;
        var readerTransaction = readerScope.Executor.ExecuteAsync<MountCreationGrantTargets>(async cancellationToken =>
        {
            readerStarted.SetResult(await GetBackendPidAsync(readerScope.Context, cancellationToken));
            return await readerScope.CreationTargets.ReadAsync(
                SpaceId, [], [MemberPublicId], cancellationToken);
        });

        try
        {
            var readerSignal = await Task.WhenAny(readerStarted.Task, readerTransaction).WaitAsync(TestTimeout);
            if (readerSignal == readerTransaction)
            {
                var earlyReaderResult = await readerTransaction;
                throw new AssertionException($"Reader completed before acquiring its lock: {earlyReaderResult}");
            }

            var readerPid = await readerStarted.Task;
            await using var observer = new NpgsqlConnection(postgres!.GetConnectionString());
            await observer.OpenAsync();
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                if (readerTransaction.IsCompleted)
                {
                    var earlyReaderResult = await readerTransaction;
                    throw new AssertionException($"Reader completed before the writer commit: {earlyReaderResult}");
                }

                var blockingPids = await observer.QuerySingleAsync<int[]>(
                    "SELECT pg_blocking_pids(@ReaderPid)", new { ReaderPid = readerPid });
                if (blockingPids.Contains(writerPid))
                {
                    break;
                }

                if (stopwatch.Elapsed >= TestTimeout)
                {
                    Assert.Fail($"Reader PID {readerPid} was not blocked by writer PID {writerPid}.");
                }

                await Task.Delay(25);
            }

            commitBarrier.SetResult();
            var readerResult = await readerTransaction.WaitAsync(TestTimeout);
            var writerResult = await writerTransaction.WaitAsync(TestTimeout);
            var targets = RequireSuccess(readerResult);
            var membership = targets.Users[MemberPublicId].Membership;

            Assert.Multiple(() =>
            {
                Assert.That(writerResult.IsSuccess, Is.True, writerResult.ToString());
                Assert.That(membership, Is.Not.Null);
                Assert.That(membership!.Id, Is.EqualTo(11));
                Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
                Assert.That(membership.Version, Is.EqualTo(4));
                Assert.That(membership.LeftAt, Is.EqualTo(leftAt));
            });
        }
        finally
        {
            commitBarrier.TrySetResult();
            await Task.WhenAll(writerTransaction, readerTransaction).WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task MountReadAsync_ShouldRestoreStoredMasksAndReturnFreshIndependentSnapshots()
    {
        await using var scope = CreateScope();
        var first = RequireSuccess(await scope.Mounts.ReadAsync(SpaceId, FilelessMountPublicId));
        Assert.That(first, Is.Not.Null);

        await using (var transaction = await sql!.BeginTransactionAsync())
        {
            await sql.ExecuteAsync("""
                UPDATE mounts
                SET version = 2, updated_at = @UpdatedAt
                WHERE id = @MountId;
                UPDATE mount_role_grants
                SET permission_mask = 5
                WHERE mount_id = @MountId AND role_id = @TargetId;
                UPDATE mount_user_grants
                SET permission_mask = 7
                WHERE mount_id = @MountId AND user_id = @TargetId;
                """, new { MountId = FilelessMountId, TargetId = MemberId, UpdatedAt = Later.AddHours(3) }, transaction);
            await transaction.CommitAsync();
        }

        var second = RequireSuccess(await scope.Mounts.ReadAsync(SpaceId, FilelessMountPublicId));
        Assert.That(second, Is.Not.Null);
        var firstRole = RequireGrant(first!, MountGrantTargetType.Role, WriterRoleId);
        var firstUser = RequireGrant(first, MountGrantTargetType.User, MemberId);
        var secondRole = RequireGrant(second!, MountGrantTargetType.Role, WriterRoleId);
        var secondUser = RequireGrant(second, MountGrantTargetType.User, MemberId);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Version, Is.EqualTo(1));
            Assert.That(first.UpdatedAt, Is.EqualTo(Later));
            Assert.That(firstRole.Permissions, Is.EquivalentTo(new[] { MountPermission.FilesWrite }));
            Assert.That(firstUser.Permissions, Is.EquivalentTo(new[] { MountPermission.FilesDownload }));
            Assert.That(second.Version, Is.EqualTo(2));
            Assert.That(second.UpdatedAt, Is.EqualTo(Later.AddHours(3)));
            Assert.That(secondRole.Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
            Assert.That(secondUser.Permissions, Is.EquivalentTo(Enum.GetValues<MountPermission>()));
            Assert.That(scope.Context.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public async Task MountReadAsync_ShouldReturnNullForWrongSpaceOrMissingMount()
    {
        await using var scope = CreateScope();

        var wrongSpace = RequireSuccess(await scope.Mounts.ReadAsync(SpaceId, OtherMountPublicId));
        var missing = RequireSuccess(await scope.Mounts.ReadAsync(
            SpaceId, new Guid("019b26d0-0000-7000-8000-000000006002")));

        Assert.Multiple(() =>
        {
            Assert.That(wrongSpace, Is.Null);
            Assert.That(missing, Is.Null);
        });
    }

    [TestCase(MountGrantTargetType.Role)]
    [TestCase(MountGrantTargetType.User)]
    public async Task GrantReadAsync_ShouldPageByTargetPublicIdAndDecodeStoredPermissions(
        MountGrantTargetType targetType)
    {
        await using var scope = CreateScope();
        var expected = ExpectedGrants(targetType);

        var first = RequireSuccess(await scope.Grants.ReadAsync(
            SpaceId, FirstMountId, targetType, null, 2));
        Assert.Multiple(() =>
        {
            Assert.That(first.Grants, Has.Count.EqualTo(2));
            Assert.That(first.NextCursor, Is.Not.Null.And.Not.Empty);
        });
        AssertGrant(first.Grants[0], targetType, expected[0]);
        AssertGrant(first.Grants[1], targetType, expected[1]);

        var second = RequireSuccess(await scope.Grants.ReadAsync(
            SpaceId, FirstMountId, targetType, first.NextCursor, 2));

        Assert.Multiple(() =>
        {
            Assert.That(second.Grants, Has.Count.EqualTo(1));
            Assert.That(second.NextCursor, Is.Null);
        });
        AssertGrant(second.Grants[0], targetType, expected[2]);
    }

    [Test]
    public async Task GrantReadAsync_ShouldRejectMalformedOrReboundCursor()
    {
        await using var scope = CreateScope();
        var page = RequireSuccess(await scope.Grants.ReadAsync(
            SpaceId, FirstMountId, MountGrantTargetType.Role, null, 1));
        var cursor = page.NextCursor;
        Assert.That(cursor, Is.Not.Null);

        var invalid = new[]
        {
            await scope.Grants.ReadAsync(SpaceId, FirstMountId, MountGrantTargetType.Role, "not-a-cursor", 1),
            await scope.Grants.ReadAsync(OtherSpaceId, FirstMountId, MountGrantTargetType.Role, cursor, 1),
            await scope.Grants.ReadAsync(SpaceId, ArchivedMountId, MountGrantTargetType.Role, cursor, 1),
            await scope.Grants.ReadAsync(SpaceId, FirstMountId, MountGrantTargetType.User, cursor, 1),
        };

        foreach (var result in invalid)
        {
            AssertError(result, ErrorCodes.Common.CursorInvalid);
        }
    }

    [Test]
    public async Task PreviewReadUserAsync_ShouldReturnOwnerWithoutMembership()
    {
        await using var scope = CreateScope();

        var owner = RequireSuccess(await scope.PreviewUsers.ReadUserAsync(SpaceId, OwnerPublicId));

        Assert.That(owner, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(owner!.User.Id, Is.EqualTo(OwnerId));
            Assert.That(owner.User.PublicId, Is.EqualTo(OwnerPublicId));
            Assert.That(owner.Membership, Is.Null);
            Assert.That(owner.Roles, Is.Empty);
        });
    }

    [Test]
    public async Task PreviewReadUserAsync_ShouldReturnMemberWithAllCurrentRoles()
    {
        await using var scope = CreateScope();

        var member = RequireSuccess(await scope.PreviewUsers.ReadUserAsync(SpaceId, MemberPublicId));

        Assert.That(member, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(member!.User.Id, Is.EqualTo(MemberId));
            Assert.That(member.Membership, Is.Not.Null);
            Assert.That(member.Membership!.Id, Is.EqualTo(11));
            Assert.That(member.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(member.Roles.Select(role => role.Id),
                Is.EquivalentTo(new[] { WriterRoleId, AdminRoleId }));
            Assert.That(member.Roles.Select(role => role.SpaceId), Is.All.EqualTo(SpaceId));
        });
    }

    [Test]
    public async Task PreviewReadCandidatesAsync_ShouldOrderUniqueScopedUsersAndResumeWithinBatch()
    {
        await using var scope = CreateScope();
        var expected = new[] { MemberPublicId, RejoinedPublicId, SuspendedPublicId, OwnerPublicId };

        var all = await ReadAllPreviewCandidatesAsync(scope, batchSize: 2);
        var first = RequireSuccess(await scope.PreviewUsers.ReadCandidatesAsync(SpaceId, null, 3));
        Assert.Multiple(() =>
        {
            Assert.That(all.Select(candidate => candidate.Subject.User.PublicId), Is.EqualTo(expected));
            Assert.That(all.Select(candidate => candidate.Subject.User.Id), Is.Unique);
            Assert.That(first.Candidates, Has.Count.EqualTo(3));
            Assert.That(first.HasMore, Is.True);
        });

        var resumed = RequireSuccess(await scope.PreviewUsers.ReadCandidatesAsync(
            SpaceId, first.Candidates[1].CursorAfter, 3));
        var rejoined = all.Single(candidate => candidate.Subject.User.Id == RejoinedId).Subject;

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Candidates.Select(candidate => candidate.Subject.User.PublicId),
                Is.EqualTo(new[] { SuspendedPublicId, OwnerPublicId }));
            Assert.That(resumed.HasMore, Is.False);
            Assert.That(rejoined.Membership!.Id, Is.EqualTo(CurrentMembershipId));
            Assert.That(rejoined.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
        });
    }

    private static async Task<MountListCandidate[]> ReadAllMountsAsync(
        ReaderScope scope,
        CurrentActor actor,
        string? sort,
        bool descending,
        int batchSize)
    {
        var candidates = new List<MountListCandidate>();
        var traversed = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        while (true)
        {
            var page = RequireSuccess(await scope.ListCandidates.ReadAsync(
                SpaceId, actor, cursor, batchSize, sort, descending));
            Assert.That(page.Candidates.Count, Is.LessThanOrEqualTo(batchSize));
            Assert.That(page.Candidates.Select(candidate => candidate.CursorAfter), Is.All.Not.Null.And.Not.Empty);
            candidates.AddRange(page.Candidates);
            if (!page.HasMore)
            {
                return candidates.ToArray();
            }

            Assert.That(page.Candidates, Is.Not.Empty);
            cursor = page.Candidates[^1].CursorAfter;
            Assert.That(traversed.Add(cursor), Is.True, "Mount list cursor did not advance.");
        }
    }

    private static async Task<MountPreviewCandidate[]> ReadAllPreviewCandidatesAsync(
        ReaderScope scope,
        int batchSize)
    {
        var candidates = new List<MountPreviewCandidate>();
        var traversed = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        while (true)
        {
            var page = RequireSuccess(await scope.PreviewUsers.ReadCandidatesAsync(SpaceId, cursor, batchSize));
            Assert.That(page.Candidates.Count, Is.LessThanOrEqualTo(batchSize));
            Assert.That(page.Candidates.Select(candidate => candidate.CursorAfter), Is.All.Not.Null.And.Not.Empty);
            candidates.AddRange(page.Candidates);
            if (!page.HasMore)
            {
                return candidates.ToArray();
            }

            Assert.That(page.Candidates, Is.Not.Empty);
            cursor = page.Candidates[^1].CursorAfter;
            Assert.That(traversed.Add(cursor), Is.True, "Mount preview cursor did not advance.");
        }
    }

    private static GrantExpectation[] ExpectedGrants(MountGrantTargetType targetType) => targetType switch
    {
        MountGrantTargetType.Role =>
        [
            new(DeletedRolePublicId, DeletedRoleId, [MountPermission.FilesRead, MountPermission.FilesDownload]),
            new(AdminRolePublicId, AdminRoleId, Enum.GetValues<MountPermission>()),
            new(WriterRolePublicId, WriterRoleId, [MountPermission.FilesWrite]),
        ],
        MountGrantTargetType.User =>
        [
            new(MemberPublicId, MemberId, [MountPermission.FilesDownload]),
            new(SuspendedPublicId, SuspendedId, [MountPermission.FilesWrite, MountPermission.FilesDownload]),
            new(OwnerPublicId, OwnerId, [MountPermission.FilesRead]),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null),
    };

    private static void AssertGrant(
        MountGrantSnapshot snapshot,
        MountGrantTargetType targetType,
        GrantExpectation expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TargetPublicId, Is.EqualTo(expected.PublicId));
            Assert.That(snapshot.Grant.TargetType, Is.EqualTo(targetType));
            Assert.That(snapshot.Grant.TargetId, Is.EqualTo(expected.TargetId));
            Assert.That(snapshot.Grant.Permissions, Is.EquivalentTo(expected.Permissions));
        });
    }

    private static MountAccessGrant RequireGrant(
        Mount mount,
        MountGrantTargetType targetType,
        long targetId) =>
        mount.Grants.Single(grant => grant.TargetType == targetType && grant.TargetId == targetId);

    private static CurrentActor CreateActor() => CurrentActor.ForSession(
        OwnerId,
        OwnerPublicId,
        SystemRole.User,
        new Guid("019b26d0-0000-7000-8000-000000004001"));

    private static async Task<int> GetBackendPidAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            "SELECT pg_backend_pid()",
            transaction: context.Database.CurrentTransaction?.GetDbTransaction(),
            cancellationToken: cancellationToken);
        return await context.Database.GetDbConnection().QuerySingleAsync<int>(command);
    }

    private static T RequireSuccess<T>(Result<T> result)
    {
        Assert.That(result.IsSuccess, Is.True, result.ToString());
        return result.Value;
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string value
            && value == expectedCode), Is.True, $"Expected error code {expectedCode} in metadata.");
    }

    private ReaderScope CreateScope() => new(services!.CreateAsyncScope());

    private sealed record GrantExpectation(Guid PublicId, long TargetId, MountPermission[] Permissions);

    private sealed class ReaderScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public AppDbContext Context => scope.ServiceProvider.GetRequiredService<AppDbContext>();
        public IMountListCandidateReader ListCandidates => scope.ServiceProvider.GetRequiredService<IMountListCandidateReader>();
        public IMountCreationGrantTargetReader CreationTargets => scope.ServiceProvider.GetRequiredService<IMountCreationGrantTargetReader>();
        public IMountReader Mounts => scope.ServiceProvider.GetRequiredService<IMountReader>();
        public IMountGrantReader Grants => scope.ServiceProvider.GetRequiredService<IMountGrantReader>();
        public IMountPreviewUserReader PreviewUsers => scope.ServiceProvider.GetRequiredService<IMountPreviewUserReader>();
        public ISpaceMembershipRepository Memberships => scope.ServiceProvider.GetRequiredService<ISpaceMembershipRepository>();
        public IUnitOfWork UnitOfWork => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        public ITransactionExecutor Executor => scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private const string SeedSql = """
        INSERT INTO users (
            id, public_id, email, normalized_email, user_name, normalized_user_name, display_name,
            password_hash, system_role, status, security_version, version, created_at, updated_at,
            suspended_at, deleted_at)
        VALUES
            (1, @OwnerPublicId, 'owner@example.com', 'OWNER@EXAMPLE.COM', 'owner', 'OWNER', 'Owner',
                'stored-password-hash', 'USER', 'ACTIVE', 1, 1, @CreatedAt, @CreatedAt, NULL, NULL),
            (2, @MemberPublicId, 'member@example.com', 'MEMBER@EXAMPLE.COM', 'member', 'MEMBER', 'Member',
                'stored-password-hash', 'USER', 'ACTIVE', 1, 1, @CreatedAt, @CreatedAt, NULL, NULL),
            (3, @SuspendedPublicId, 'suspended@example.com', 'SUSPENDED@EXAMPLE.COM', 'suspended', 'SUSPENDED', 'Suspended',
                'stored-password-hash', 'USER', 'SUSPENDED', 2, 2, @CreatedAt, @SuspendedAt, @SuspendedAt, NULL),
            (4, @RejoinedPublicId, 'rejoined@example.com', 'REJOINED@EXAMPLE.COM', 'rejoined', 'REJOINED', 'Rejoined',
                'stored-password-hash', 'USER', 'ACTIVE', 1, 1, @CreatedAt, @CreatedAt, NULL, NULL),
            (5, @OtherOwnerPublicId, 'other-owner@example.com', 'OTHER-OWNER@EXAMPLE.COM', 'otherowner', 'OTHEROWNER', 'Other owner',
                'stored-password-hash', 'USER', 'ACTIVE', 1, 1, @CreatedAt, @CreatedAt, NULL, NULL),
            (6, @OtherMemberPublicId, 'other-member@example.com', 'OTHER-MEMBER@EXAMPLE.COM', 'othermember', 'OTHERMEMBER', 'Other member',
                'stored-password-hash', 'USER', 'ACTIVE', 1, 1, @CreatedAt, @CreatedAt, NULL, NULL);

        INSERT INTO spaces (
            id, public_id, owner_user_id, name, description, status, storage_allowed_bytes,
            storage_used_bytes, storage_reserved_bytes, version, created_at, updated_at,
            archived_at, deleting_at, deleted_at)
        VALUES
            (100, '019b26d0-0000-7000-8000-000000001100', 1, 'Primary', NULL, 'ACTIVE', NULL, 0, 0, 1,
                @CreatedAt, @CreatedAt, NULL, NULL, NULL),
            (200, '019b26d0-0000-7000-8000-000000001200', 5, 'Other', NULL, 'ACTIVE', NULL, 0, 0, 1,
                @CreatedAt, @CreatedAt, NULL, NULL, NULL);

        INSERT INTO space_roles (
            id, public_id, space_id, name, normalized_name, system_key, color, position,
            is_managed, permission_mask, version, created_at, updated_at, deleted_at)
        VALUES
            (2, @WriterRolePublicId, 100, 'Writer', 'WRITER', NULL, NULL, 10, false, 8, 2,
                @CreatedAt, @CreatedAt, NULL),
            (3, @DeletedRolePublicId, 100, 'Viewer', 'VIEWER', 'VIEWER', NULL, 0, true, 21, 3,
                @CreatedAt, @RoleDeletedAt, @RoleDeletedAt),
            (4, @AdminRolePublicId, 100, 'Admin', 'ADMIN', 'ADMIN', NULL, 2, true, 8191, 1,
                @CreatedAt, @CreatedAt, NULL),
            (5, @ForeignRolePublicId, 200, 'Foreign', 'FOREIGN', NULL, NULL, 0, false, 1, 1,
                @CreatedAt, @CreatedAt, NULL);

        INSERT INTO space_memberships (
            id, public_id, space_id, user_id, status, version, joined_at, updated_at, suspended_at, left_at)
        VALUES
            (11, '019b26d0-0000-7000-8000-000000003011', 100, 2, 'ACTIVE', 3,
                @CreatedAt, @MemberUpdatedAt, NULL, NULL),
            (12, '019b26d0-0000-7000-8000-000000003012', 100, 3, 'SUSPENDED', 4,
                @CreatedAt, @SuspendedAt, @SuspendedAt, NULL),
            (13, '019b26d0-0000-7000-8000-000000003013', 100, 4, 'LEFT', 2,
                @PriorJoinedAt, @PriorLeftAt, NULL, @PriorLeftAt),
            (14, '019b26d0-0000-7000-8000-000000003014', 100, 4, 'ACTIVE', 5,
                @CreatedAt, @RejoinedUpdatedAt, NULL, NULL),
            (15, '019b26d0-0000-7000-8000-000000003015', 200, 6, 'ACTIVE', 1,
                @CreatedAt, @CreatedAt, NULL, NULL);

        INSERT INTO space_membership_roles (membership_id, role_id, space_id, assigned_by_user_id, assigned_at)
        VALUES
            (11, 2, 100, 1, @CreatedAt),
            (11, 4, 100, 1, @MemberUpdatedAt),
            (12, 3, 100, 1, @CreatedAt),
            (13, 4, 100, 1, @PriorJoinedAt),
            (14, 4, 100, 1, @CreatedAt),
            (15, 5, 200, 5, @CreatedAt);

        INSERT INTO mounts (
            id, public_id, space_id, name, visibility, status, version, created_at, updated_at, archived_at)
        VALUES
            (101, @FirstMountPublicId, 100, 'Private active', 'PRIVATE', 'ACTIVE', 1,
                @CreatedAt, @CreatedAt, NULL),
            (102, @ArchivedMountPublicId, 100, 'Archived', 'PUBLIC', 'ARCHIVED', 7,
                @CreatedAt, @MountArchivedAt, @MountArchivedAt),
            (103, @FilelessMountPublicId, 100, 'Fileless private', 'PRIVATE', 'ACTIVE', 1,
                @Later, @Later, NULL),
            (104, @LastMountPublicId, 100, 'Later archived', 'PUBLIC', 'ARCHIVED', 2,
                @Later, @LastMountArchivedAt, @LastMountArchivedAt),
            (201, @OtherMountPublicId, 200, 'Other space', 'PUBLIC', 'ACTIVE', 1,
                @Later, @Later, NULL);

        INSERT INTO mount_role_grants (mount_id, space_id, role_id, permission_mask)
        VALUES
            (101, 100, 2, 2),
            (101, 100, 3, 5),
            (101, 100, 4, 7),
            (102, 100, 3, 5),
            (103, 100, 2, 2),
            (104, 100, 4, 6),
            (201, 200, 5, 1);

        INSERT INTO mount_user_grants (mount_id, user_id, permission_mask)
        VALUES
            (101, 1, 1),
            (101, 2, 4),
            (101, 3, 6),
            (102, 3, 6),
            (103, 2, 4),
            (104, 2, 2),
            (201, 6, 1);

        """;
}
