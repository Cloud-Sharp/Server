using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.Creations;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Creations;

[TestFixture]
public sealed class CreateMountUseCaseTests
{
    private const long SpaceId = 7;
    private const long SpaceOwnerUserId = 21;
    private const long AdminRoleId = 101;
    private const long RoleTargetId = 102;
    private const long UserTargetId = 43;
    private const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 30, 0, TimeSpan.Zero);
    private static readonly Guid OtherUserPublicId = Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327845");
    private static readonly Guid MissingRolePublicId = Guid.Parse("01977b60-28f3-7ab5-902d-cf0f5d6a1002");
    private static readonly Guid OutOfSpaceRolePublicId = Guid.Parse("01977b60-28f3-7ab5-902d-cf0f5d6a1003");
    private static readonly Guid MissingUserPublicId = Guid.Parse("01977b60-28f3-7ab5-902d-cf0f5d6a1004");
    private static readonly Guid OutOfSpaceUserPublicId = Guid.Parse("01977b60-28f3-7ab5-902d-cf0f5d6a1005");

    [Test]
    public async Task ExecuteAsync_ShouldCreateActiveMountForOwnerWithoutMembership()
    {
        // Arrange
        var harness = OwnerHarness();
        var command = ValidCommand(harness);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var mount = harness.Writer.Registrations.Single();
        Assert.That(mount.Overwrites, Is.Empty);
        AssertCreatedMount(harness, result, MountScopedPermissions.All);
    }

    [TestCase("None")]
    [TestCase("Role")]
    [TestCase("User")]
    public async Task ExecuteAsync_ShouldCreateMountWithInitialOverwritesAndAdministratorAccess(string grantTarget)
    {
        // Arrange
        var setup = AdministratorHarness();
        var harness = setup.Harness;
        var roleTarget = grantTarget == "Role" ? setup.AdminRole : setup.RoleTarget;
        var userTarget = grantTarget == "User" ? setup.ActorUser : setup.UserTarget;
        var userMembership = grantTarget == "User" ? setup.ActorMembership : setup.UserMembership;
        SeedTargets(setup, roleTarget, userTarget, userMembership);
        var command = GrantCommand(harness, roleTarget.PublicId, userTarget.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var mount = harness.Writer.Registrations.Single();
        var roleOverwrite = mount.Overwrites.Single(overwrite => overwrite.TargetType == MountOverwriteTargetType.Role);
        var userOverwrite = mount.Overwrites.Single(overwrite => overwrite.TargetType == MountOverwriteTargetType.User);
        Assert.Multiple(() =>
        {
            Assert.That(mount.Overwrites, Has.Count.EqualTo(2));
            Assert.That(roleOverwrite.TargetId, Is.EqualTo(roleTarget.Id));
            Assert.That(roleOverwrite.Allow, Is.EquivalentTo(new[] { SpacePermission.MountManage }));
            Assert.That(roleOverwrite.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
            Assert.That(userOverwrite.TargetId, Is.EqualTo(userTarget.Id));
            Assert.That(userOverwrite.Allow, Is.EquivalentTo(new[] { SpacePermission.MountManage }));
            Assert.That(userOverwrite.Deny, Is.Empty);
            Assert.That(harness.TargetReader.ReadCount, Is.EqualTo(1));
            Assert.That(harness.TargetReader.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(harness.TargetReader.RequestedRolePublicIds, Is.EquivalentTo(new[] { roleTarget.PublicId }));
            Assert.That(harness.TargetReader.RequestedUserPublicIds, Is.EquivalentTo(new[] { userTarget.PublicId }));
            Assert.That(harness.TargetReader.WasInTransaction, Is.True);
        });

        AssertCreatedMount(harness, result, MountScopedPermissions.All);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPermitCreationWithDelegatedSpacePermission()
    {
        // Arrange
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var snapshot = TestSpaceAuthorizationSnapshots.MemberSnapshot(
            permissions: [SpacePermission.MountManage, SpacePermission.MountView]);
        var harness = new Harness(actor, snapshot);
        var user = CreateUser(actor.UserId, actor.UserPublicId);
        harness.TargetReader.Users[user.PublicId] = new MountCreationUserTarget(user, snapshot.Membership);
        var command = ValidCommand(
            harness,
            userOverwrites: [new InitialMountUserOverwrite(user.PublicId, [SpacePermission.MountView], [])]);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertCreatedMount(harness, result, [SpacePermission.MountView, SpacePermission.MountManage]);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPermissionDeniedForInitialOverwriteBeyondActorPermissions()
    {
        // Arrange
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var snapshot = TestSpaceAuthorizationSnapshots.MemberSnapshot(
            permissions: [SpacePermission.MountManage, SpacePermission.MountView]);
        var harness = new Harness(actor, snapshot);
        var user = CreateUser(actor.UserId, actor.UserPublicId);
        harness.TargetReader.Users[user.PublicId] = new MountCreationUserTarget(user, snapshot.Membership);
        var command = ValidCommand(
            harness,
            userOverwrites: [new InitialMountUserOverwrite(user.PublicId, [SpacePermission.FilesWrite], [])]);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        Assert.That(harness.TargetReader.ReadCount, Is.EqualTo(1));
        AssertRejectedInsideTransaction(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFoundWithoutStagingChanges()
    {
        // Arrange
        var harness = OwnerHarness(status: null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceNotFound);
        Assert.That(harness.TargetReader.ReadCount, Is.Zero);
        AssertRejectedInsideTransaction(harness);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    public async Task ExecuteAsync_ShouldReturnActualSpaceStateWithoutStagingChanges(SpaceStatus status)
    {
        // Arrange
        var harness = OwnerHarness(status);

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(result.Errors.Single(), Is.TypeOf<SpaceAuthorizationError>()
            .With.Property(nameof(SpaceAuthorizationError.ActualSpaceStatus)).EqualTo(status));
        Assert.That(harness.TargetReader.ReadCount, Is.Zero);
        AssertRejectedInsideTransaction(harness);
    }

    [TestCase("MissingRole")]
    [TestCase("OutOfSpaceRole")]
    [TestCase("MissingUser")]
    [TestCase("OutOfSpaceUser")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundForInvalidInitialTarget(string scenario)
    {
        // Arrange
        var setup = AdministratorHarness();
        var command = InvalidTargetCommand(setup, scenario);
        var harness = setup.Harness;

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ResourceNotFound);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TargetReader.ReadCount, Is.EqualTo(1));
            Assert.That(harness.TargetReader.WasInTransaction, Is.True);
        });
        AssertRejectedInsideTransaction(harness);
    }

    [TestCase("DeletedRole")]
    [TestCase("SuspendedMember")]
    public async Task ExecuteAsync_ShouldReturnRequestConflictForIneligibleInitialTarget(string scenario)
    {
        // Arrange
        var setup = AdministratorHarness();
        var command = InvalidTargetCommand(setup, scenario);
        var harness = setup.Harness;

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.RequestConflict);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TargetReader.ReadCount, Is.EqualTo(1));
            Assert.That(harness.TargetReader.WasInTransaction, Is.True);
        });
        AssertRejectedInsideTransaction(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var harness = OwnerHarness();
        var command = ValidCommand(harness, name: "  ");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(ErrorCodes.Mount.NameInvalid));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(nameof(CreateMountCommand.Name)));
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.TargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Writer.Registrations, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
            Assert.That(harness.Events, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackRegistrationFailureWithoutSaving()
    {
        // Arrange
        var harness = OwnerHarness();
        var failure = CommonError.DependencyUnavailable();
        harness.Writer.Failure = failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(harness.Writer.Registrations, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "register" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveFailureWithoutCommitting()
    {
        // Arrange
        var harness = OwnerHarness();
        var failure = CommonError.DependencyUnavailable();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(harness.Writer.Registrations, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "register", "save" }));
        });
    }

    private static void AssertCreatedMount(
        Harness harness,
        Result<MountResultDto> result,
        IEnumerable<SpacePermission> expectedPermissions)
    {
        var mount = harness.Writer.Registrations.Single();
        var dto = result.Value.Mount;
        Assert.Multiple(() =>
        {
            Assert.That(mount.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(mount.Name, Is.EqualTo("Design"));
            Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Private));
            Assert.That(mount.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(mount.Version, Is.EqualTo(1));
            Assert.That(mount.CreatedAt, Is.EqualTo(Now));
            Assert.That(mount.UpdatedAt, Is.EqualTo(Now));
            Assert.That(mount.ArchivedAt, Is.Null);
            Assert.That(dto.SpacePublicId, Is.EqualTo(TestSpaceAuthorizationSnapshots.SpacePublicId));
            Assert.That(dto.MountPublicId, Is.EqualTo(mount.PublicId));
            Assert.That(dto.Name, Is.EqualTo(mount.Name));
            Assert.That(dto.Visibility, Is.EqualTo(mount.Visibility));
            Assert.That(dto.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(expectedPermissions));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(dto.CreatedAt, Is.EqualTo(Now));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.ArchivedAt, Is.Null);
            Assert.That(result.Value.Version, Is.EqualTo(1));
            Assert.That(harness.AuthorizationReader.Reads, Is.EqualTo(
                new[] { (TestSpaceAuthorizationSnapshots.SpacePublicId, harness.Actor.UserId) }));
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.Writer.WasInTransaction, Is.True);
            Assert.That(harness.Events, Is.EqualTo(new[] { "register", "save" }));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertRejectedInsideTransaction(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.Writer.Registrations, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.Events, Is.Empty);
        });
    }

    private static void AssertError(Result<MountResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(errorCode));
    }

    private static Harness OwnerHarness(SpaceStatus? status = SpaceStatus.Active)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var snapshot = status is null
            ? null
            : new SpaceAuthorizationSnapshot(
                TestSpaceAuthorizationSnapshots.Space(status.Value, ownerUserId: actor.UserId, id: SpaceId),
                null,
                []);
        return new Harness(actor, snapshot);
    }

    private static AdministratorSetup AdministratorHarness(
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var adminRole = TestSpaceAuthorizationSnapshots.Role(
            id: AdminRoleId,
            spaceId: SpaceId,
            systemKey: SpaceRoleSystemKey.Admin);
        var actorMembership = TestSpaceAuthorizationSnapshots.Membership(
            membershipStatus,
            userId: actor.UserId,
            spaceId: SpaceId,
            roleIds: adminRole.Id);
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(ownerUserId: SpaceOwnerUserId, id: SpaceId),
            actorMembership,
            [adminRole]);
        var harness = new Harness(actor, snapshot);
        var roleTarget = TestSpaceAuthorizationSnapshots.Role(id: RoleTargetId, spaceId: SpaceId);
        var userTarget = CreateUser(UserTargetId, OtherUserPublicId);
        var userMembership = TestSpaceAuthorizationSnapshots.Membership(
            userId: userTarget.Id,
            spaceId: SpaceId,
            id: 56,
            roleIds: roleTarget.Id);
        var actorUser = CreateUser(actor.UserId, actor.UserPublicId);
        return new AdministratorSetup(
            harness,
            adminRole,
            roleTarget,
            userTarget,
            userMembership,
            actorUser,
            actorMembership);
    }

    private static void SeedTargets(
        AdministratorSetup setup,
        SpaceRole roleTarget,
        User userTarget,
        SpaceMembership userMembership)
    {
        setup.Harness.TargetReader.Roles[roleTarget.PublicId] = roleTarget;
        setup.Harness.TargetReader.Users[userTarget.PublicId] = new MountCreationUserTarget(
            userTarget,
            userMembership);
    }

    private static CreateMountCommand InvalidTargetCommand(AdministratorSetup setup, string scenario)
    {
        var harness = setup.Harness;
        SeedTargets(setup, setup.RoleTarget, setup.UserTarget, setup.UserMembership);
        var rolePublicId = setup.RoleTarget.PublicId;
        var userPublicId = setup.UserTarget.PublicId;
        switch (scenario)
        {
            case "MissingRole":
                rolePublicId = MissingRolePublicId;
                break;
            case "OutOfSpaceRole":
                rolePublicId = OutOfSpaceRolePublicId;
                break;
            case "DeletedRole":
                var deletedRole = TestSpaceAuthorizationSnapshots.Role(
                    id: RoleTargetId + 1,
                    spaceId: SpaceId,
                    deletedAt: Now.AddDays(-18));
                rolePublicId = deletedRole.PublicId;
                harness.TargetReader.Roles[rolePublicId] = deletedRole;
                break;
            case "MissingUser":
                userPublicId = MissingUserPublicId;
                break;
            case "OutOfSpaceUser":
                var unrelatedUser = CreateUser(UserTargetId + 1, OutOfSpaceUserPublicId);
                userPublicId = unrelatedUser.PublicId;
                harness.TargetReader.Users[userPublicId] = new MountCreationUserTarget(unrelatedUser, null);
                break;
            case "SuspendedMember":
                var suspendedMembership = TestSpaceAuthorizationSnapshots.Membership(
                    SpaceMembershipStatus.Suspended,
                    userId: setup.UserTarget.Id,
                    spaceId: SpaceId,
                    id: 56,
                    roleIds: setup.RoleTarget.Id);
                harness.TargetReader.Users[userPublicId] = new MountCreationUserTarget(
                    setup.UserTarget,
                    suspendedMembership);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        return GrantCommand(harness, rolePublicId, userPublicId);
    }

    private static CreateMountCommand GrantCommand(
        Harness harness,
        Guid rolePublicId,
        Guid userPublicId) =>
        ValidCommand(
            harness,
            roleOverwrites: [new InitialMountRoleOverwrite(rolePublicId, [SpacePermission.MountManage], [SpacePermission.FilesWrite])],
            userOverwrites: [new InitialMountUserOverwrite(userPublicId, [SpacePermission.MountManage], [])]);

    private static CreateMountCommand ValidCommand(
        Harness harness,
        IReadOnlyList<InitialMountRoleOverwrite>? roleOverwrites = null,
        IReadOnlyList<InitialMountUserOverwrite>? userOverwrites = null,
        string name = "  Design  ") =>
        new(
            harness.SpaceContext(),
            name,
            MountVisibility.Private,
            roleOverwrites,
            userOverwrites);

    private static User CreateUser(long id, Guid publicId) =>
        User.Reconstitute(
            id,
            publicId,
            EmailAddress.Create($"user{id}@example.com").Value,
            NormalizedName.Create($"user{id}").Value,
            "Target User",
            "hashed-password",
            SystemRole.User,
            UserStatus.Active,
            1,
            1,
            Now.AddDays(-30),
            Now.AddDays(-1),
            null,
            null);

    private sealed record AdministratorSetup(
        Harness Harness,
        SpaceRole AdminRole,
        SpaceRole RoleTarget,
        User UserTarget,
        SpaceMembership UserMembership,
        User ActorUser,
        SpaceMembership ActorMembership);

    private sealed class Harness
    {
        private readonly SpaceAuthorizationSnapshot? snapshot;

        public Harness(CurrentActor actor, SpaceAuthorizationSnapshot? snapshot)
        {
            this.snapshot = snapshot;
            Actor = actor;
            AuthorizationReader = new AuthorizationReader(PersistenceSession, snapshot);
            TargetReader = new GrantTargetReader(PersistenceSession);
            Writer = new MountWriter(PersistenceSession, Events);
            UnitOfWork = new FakeUnitOfWork(PersistenceSession);
            UnitOfWork.BeforeSaveChanges = _ => Events.Add("save");
            TransactionExecutor = new FakeTransactionExecutor(PersistenceSession);
            Clock = new FakeClock(Now);
        }

        public CurrentActor Actor { get; }
        public FakePersistenceSession PersistenceSession { get; } = new();

        /// <summary>endpoint의 Space 필터가 넘긴 context. snapshot이 없으면 모든 권한을 가진 것으로 둔다.</summary>
        public AuthorizedSpaceContext SpaceContext()
        {
            var isOwner = snapshot?.Space.OwnerUserId == Actor.UserId;
            var permissions = snapshot is null
                ? Enum.GetValues<SpacePermission>()
                : SpacePermissionPolicy.Evaluate(snapshot.Space, Actor.UserId, snapshot.Membership, snapshot.Roles)
                    .ToArray();
            return new AuthorizedSpaceContext(
                SpaceId,
                TestSpaceAuthorizationSnapshots.SpacePublicId,
                Actor.ActorType,
                Actor.UserId,
                isOwner ? null : snapshot?.Membership?.Id,
                permissions,
                isOwner ? SpaceAuthorizationSource.Owner : SpaceAuthorizationSource.Membership);
        }

        public AuthorizationReader AuthorizationReader { get; }
        public GrantTargetReader TargetReader { get; }
        public MountWriter Writer { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor TransactionExecutor { get; }
        public FakeClock Clock { get; }
        public List<string> Events { get; } = [];

        public CreateMountUseCase UseCase => new(
            AuthorizationReader,
            TargetReader,
            Writer,
            UnitOfWork,
            TransactionExecutor,
            Clock);
    }

    private sealed class AuthorizationReader(
        FakePersistenceSession session,
        SpaceAuthorizationSnapshot? snapshot) : ISpaceAuthorizationReader
    {
        public List<(Guid SpacePublicId, long UserId)> Reads { get; } = [];
        public bool WasInTransaction { get; private set; }

        public Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
            Guid spacePublicId,
            long userId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((spacePublicId, userId));
            WasInTransaction = session.IsTransactionActive;
            return Task.FromResult(Result.Ok(snapshot));
        }
    }

    private sealed class GrantTargetReader(FakePersistenceSession session) : IMountCreationGrantTargetReader
    {
        public Dictionary<Guid, SpaceRole> Roles { get; } = [];
        public Dictionary<Guid, MountCreationUserTarget> Users { get; } = [];
        public int ReadCount { get; private set; }
        public long? SpaceId { get; private set; }
        public IReadOnlyCollection<Guid>? RequestedRolePublicIds { get; private set; }
        public IReadOnlyCollection<Guid>? RequestedUserPublicIds { get; private set; }
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
            return Task.FromResult(Result.Ok(new MountCreationGrantTargets(Roles, Users)));
        }
    }

    private sealed class MountWriter(
        FakePersistenceSession session,
        List<string> events) : IMountCreationWriter
    {
        public List<Mount> Registrations { get; } = [];
        public IError? Failure { get; set; }
        public bool WasInTransaction { get; private set; }

        public Task<Result> AddAsync(
            Mount mount,
            CancellationToken cancellationToken = default)
        {
            Registrations.Add(mount);
            events.Add("register");
            WasInTransaction = session.IsTransactionActive;
            return Task.FromResult(Failure is { } failure
                ? Result.Fail(failure)
                : Result.Ok());
        }
    }
}
