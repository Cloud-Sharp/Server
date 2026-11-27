using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.Archives;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using CloudSharp.Core.UseCases.Mounts.Reactivations;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Mounts;

internal sealed class MountMutationTestHarness
{
    public const long MountId = 301;
    public const long RoleTargetId = 102;
    public const long OtherRoleTargetId = 103;
    public const long UserTargetId = 43;
    public const long OtherUserTargetId = 44;

    private MountMutationTestHarness(
        CurrentActor actor,
        SpaceAuthorizationSnapshot? authorizationSnapshot,
        Mount? storedMount)
    {
        Actor = actor;
        AuthorizationReader = new RecordingAuthorizationReader(PersistenceSession, authorizationSnapshot, Events);
        MountReader = new RecordingMountReader(this);
        GrantTargetReader = new RecordingGrantTargetReader(PersistenceSession, Events);
        Writer = new RecordingMutationWriter(PersistenceSession, Events);
        UnitOfWork = new FakeUnitOfWork(PersistenceSession);
        UnitOfWork.BeforeSaveChanges = _ => Events.Add("save");
        TransactionExecutor = new FakeTransactionExecutor(PersistenceSession);
        Clock = new FakeClock(TestMountReadSnapshots.Now);
        StoredMount = storedMount;
    }

    public CurrentActor Actor { get; }
    public FakePersistenceSession PersistenceSession { get; } = new();
    public RecordingAuthorizationReader AuthorizationReader { get; }
    public RecordingMountReader MountReader { get; }
    public RecordingGrantTargetReader GrantTargetReader { get; }
    public RecordingMutationWriter Writer { get; }
    public FakeUnitOfWork UnitOfWork { get; }
    public FakeTransactionExecutor TransactionExecutor { get; }
    public FakeClock Clock { get; }
    public List<string> Events { get; } = [];
    public Mount? StoredMount { get; set; }
    public Mount? LastReadMount { get; set; }

    public UpdateMountUseCase UpdateUseCase => new(
        AuthorizationReader,
        MountReader,
        Writer,
        UnitOfWork,
        TransactionExecutor,
        Clock);

    public ArchiveMountUseCase ArchiveUseCase => new(
        AuthorizationReader,
        MountReader,
        Writer,
        UnitOfWork,
        TransactionExecutor,
        Clock);

    public ReactivateMountUseCase ReactivateUseCase => new(
        AuthorizationReader,
        MountReader,
        Writer,
        UnitOfWork,
        TransactionExecutor,
        Clock);

    public ReplaceMountRolePermissionsUseCase ReplaceRolePermissionsUseCase => new(
        AuthorizationReader,
        MountReader,
        GrantTargetReader,
        Writer,
        UnitOfWork,
        TransactionExecutor,
        Clock);

    public ReplaceMountUserPermissionsUseCase ReplaceUserPermissionsUseCase => new(
        AuthorizationReader,
        MountReader,
        GrantTargetReader,
        Writer,
        UnitOfWork,
        TransactionExecutor,
        Clock);

    public static MountMutationTestHarness Owner(
        Mount? mount,
        SpaceStatus spaceStatus = SpaceStatus.Active)
    {
        var actor = TestMountReadSnapshots.Actor();
        return new MountMutationTestHarness(
            actor,
            TestMountReadSnapshots.Owner(actor, spaceStatus),
            mount);
    }

    public static MountMutationTestHarness Administrator(
        Mount? mount,
        SpaceStatus spaceStatus = SpaceStatus.Active)
    {
        var actor = TestMountReadSnapshots.Actor();
        var adminRole = TestMountReadSnapshots.Role(
            101,
            systemKey: SpaceRoleSystemKey.Admin);
        var snapshot = TestMountReadSnapshots.Member(
            actor,
            roles: [adminRole],
            assignedRoleIds: [adminRole.Id],
            spaceStatus: spaceStatus);
        return new MountMutationTestHarness(actor, snapshot, mount);
    }

    public static MountMutationTestHarness OrdinaryMember(Mount? mount)
    {
        var actor = TestMountReadSnapshots.Actor();
        return new MountMutationTestHarness(
            actor,
            TestMountReadSnapshots.Member(actor),
            mount);
    }

    public SpaceRole SeedRole(
        long id = RoleTargetId,
        long spaceId = TestMountReadSnapshots.SpaceId,
        DateTimeOffset? deletedAt = null)
    {
        var role = TestMountReadSnapshots.Role(id, spaceId, deletedAt: deletedAt);
        GrantTargetReader.Roles[role.PublicId] = role;
        return role;
    }

    public User SeedUser(
        long id = UserTargetId,
        UserStatus userStatus = UserStatus.Active,
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active,
        long? membershipSpaceId = TestMountReadSnapshots.SpaceId)
    {
        var user = TestMountReadSnapshots.User(id, userStatus);
        var membership = membershipSpaceId is long spaceId
            ? TestMountReadSnapshots.Membership(
                id,
                spaceId,
                membershipStatus)
            : null;
        GrantTargetReader.Users[user.PublicId] = new MountCreationUserTarget(user, membership);
        return user;
    }

    public static Mount Copy(Mount mount) =>
        Mount.Reconstitute(
            mount.Id,
            mount.PublicId,
            mount.SpaceId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            mount.Version,
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt,
            mount.Grants);

    public static void AssertError<T>(Result<T> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(errorCode));
    }

    public static void AssertValidationError<T>(
        Result<T> result,
        string errorCode,
        string propertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(propertyName));
        });
    }

    public static void AssertPermissionResult(
        Result<MountPermissionResultDto> result,
        MountGrantTargetType targetType,
        Guid targetPublicId,
        IEnumerable<MountPermission> expectedPermissions,
        long expectedVersion)
    {
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Grant.TargetType, Is.EqualTo(targetType));
            Assert.That(result.Value.Grant.TargetPublicId, Is.EqualTo(targetPublicId));
            Assert.That(result.Value.Grant.Permissions, Is.EquivalentTo(expectedPermissions));
            Assert.That(result.Value.Version, Is.EqualTo(expectedVersion));
        });
    }

    public static void AssertNoDependencyCalls(MountMutationTestHarness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.MountReader.Reads, Is.Empty);
            Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Writer.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
            Assert.That(harness.Events, Is.Empty);
        });
    }

    public static void AssertRejectedWithoutWrites(MountMutationTestHarness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Writer.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    public static void AssertNoOp(MountMutationTestHarness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Writer.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    public static void AssertSuccessfulWrite(
        MountMutationTestHarness harness,
        long expectedVersion)
    {
        Assert.That(harness.Writer.UpdateCalls, Has.Count.EqualTo(1));
        var update = harness.Writer.UpdateCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.ExpectedVersion, Is.EqualTo(expectedVersion));
            Assert.That(update.Mount.Version, Is.EqualTo(expectedVersion + 1));
            Assert.That(harness.Writer.WasInTransaction, Is.True);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    public static void AssertStagingFailure<T>(
        MountMutationTestHarness harness,
        Result<T> result,
        IError failure)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(harness.Writer.UpdateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    public static void AssertSaveFailure<T>(
        MountMutationTestHarness harness,
        Result<T> result,
        IError failure)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(harness.Writer.UpdateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    public sealed class RecordingAuthorizationReader(
        FakePersistenceSession session,
        SpaceAuthorizationSnapshot? snapshot,
        List<string> events) : ISpaceAuthorizationReader
    {
        public List<(Guid SpacePublicId, long UserId)> Reads { get; } = [];
        public SpaceAuthorizationSnapshot? Snapshot { get; set; } = snapshot;
        public IError? Failure { get; set; }
        public bool WasInTransaction { get; private set; }

        public Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
            Guid spacePublicId,
            long userId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((spacePublicId, userId));
            WasInTransaction = session.IsTransactionActive;
            events.Add("authorize");
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<SpaceAuthorizationSnapshot?>(failure)
                : Result.Ok(Snapshot));
        }
    }

    public sealed class RecordingMountReader(MountMutationTestHarness harness) : IMountReader
    {
        public List<(long SpaceId, Guid MountPublicId)> Reads { get; } = [];
        public IError? Failure { get; set; }
        public bool WasInTransaction { get; private set; }

        public Task<Result<Mount?>> ReadAsync(
            long spaceId,
            Guid mountPublicId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((spaceId, mountPublicId));
            WasInTransaction = harness.PersistenceSession.IsTransactionActive;
            harness.Events.Add("read-mount");
            if (Failure is { } failure)
            {
                return Task.FromResult(Result.Fail<Mount?>(failure));
            }

            harness.LastReadMount = harness.StoredMount is { } mount ? Copy(mount) : null;
            return Task.FromResult(Result.Ok(harness.LastReadMount));
        }
    }

    public sealed class RecordingGrantTargetReader(
        FakePersistenceSession session,
        List<string> events) : IMountCreationGrantTargetReader
    {
        public Dictionary<Guid, SpaceRole> Roles { get; } = [];
        public Dictionary<Guid, MountCreationUserTarget> Users { get; } = [];
        public int ReadCount { get; private set; }
        public long? SpaceId { get; private set; }
        public IReadOnlyCollection<Guid> RequestedRolePublicIds { get; private set; } = [];
        public IReadOnlyCollection<Guid> RequestedUserPublicIds { get; private set; } = [];
        public IError? Failure { get; set; }
        public bool WasInTransaction { get; private set; }

        public Task<Result<MountCreationGrantTargets>> ReadAsync(
            long spaceId,
            IReadOnlyCollection<Guid> rolePublicIds,
            IReadOnlyCollection<Guid> userPublicIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            SpaceId = spaceId;
            RequestedRolePublicIds = rolePublicIds.ToArray();
            RequestedUserPublicIds = userPublicIds.ToArray();
            WasInTransaction = session.IsTransactionActive;
            events.Add("read-targets");
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<MountCreationGrantTargets>(failure)
                : Result.Ok(new MountCreationGrantTargets(Roles, Users)));
        }
    }

    public sealed class RecordingMutationWriter(
        FakePersistenceSession session,
        List<string> events) : IMountMutationWriter
    {
        public List<(Mount Mount, long ExpectedVersion)> UpdateCalls { get; } = [];
        public IError? Failure { get; set; }
        public bool WasInTransaction { get; private set; }

        public Task<Result> UpdateAsync(
            Mount mount,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add((mount, expectedVersion));
            WasInTransaction = session.IsTransactionActive;
            events.Add("stage");
            return Task.FromResult(Failure is { } failure ? Result.Fail(failure) : Result.Ok());
        }
    }
}
