using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.OwnershipTransfers;

[TestFixture]
public class TransferSpaceOwnershipUseCaseTests
{
    private const long SpaceId = 7;
    private const long OwnerUserId = 21;
    private const long NewOwnerUserId = 42;
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 5, 20, 0, TimeSpan.Zero);
    private static readonly Guid OwnerUserPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec001");
    private static readonly Guid NewOwnerUserPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec100");
    private static readonly Guid RootFolderPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec101");
    private static readonly Guid MembershipPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec102");
    private static readonly Guid AdminRolePublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec103");
    private static readonly Guid OtherOwnerPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec104");

    private sealed record MembershipDeletion(SpaceMembership Membership, long ExpectedVersion);

    private sealed class FakeSpaceOwnershipMembershipRepository : ISpaceOwnershipMembershipRepository
    {
        private readonly List<(long SpaceId, long UserId)> membershipLookups = new();
        private readonly List<long> managedAdminRoleSpaceIds = new();
        private readonly List<MembershipDeletion> deleteCalls = new();
        private readonly List<SpaceMembership> addCalls = new();

        public SpaceMembership? CurrentMembership { get; set; }

        public SpaceRole? ManagedAdminRole { get; set; }

        public Result DeleteResult { get; set; } = Result.Ok();

        public Result AddResult { get; set; } = Result.Ok();

        public IReadOnlyList<(long SpaceId, long UserId)> MembershipLookups => membershipLookups;

        public IReadOnlyList<long> ManagedAdminRoleSpaceIds => managedAdminRoleSpaceIds;

        public IReadOnlyList<MembershipDeletion> DeleteCalls => deleteCalls;

        public IReadOnlyList<SpaceMembership> AddCalls => addCalls;

        public Task<Result<SpaceMembership?>> FindByUserIdAsync(
            long spaceId,
            long userId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            membershipLookups.Add((spaceId, userId));
            var membership = CurrentMembership;
            var found = membership is not null
                && membership.SpaceId == spaceId
                && membership.UserId == userId
                && membership.Status is SpaceMembershipStatus.Active or SpaceMembershipStatus.Suspended
                ? membership
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result<SpaceRole?>> FindManagedAdminRoleAsync(
            long spaceId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            managedAdminRoleSpaceIds.Add(spaceId);
            var role = ManagedAdminRole;
            var found = role is not null
                && role.SpaceId == spaceId
                && role.IsManaged
                && role.SystemKey == SpaceRoleSystemKey.Admin
                && role.DeletedAt is null
                ? role
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result> DeleteAsync(
            SpaceMembership membership,
            long expectedVersion,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(membership);
            ct.ThrowIfCancellationRequested();
            deleteCalls.Add(new MembershipDeletion(membership, expectedVersion));
            return Task.FromResult(DeleteResult);
        }

        public Task<Result> AddAsync(
            SpaceMembership membership,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(membership);
            ct.ThrowIfCancellationRequested();
            addCalls.Add(membership);
            return Task.FromResult(AddResult);
        }
    }

    private sealed class Harness
    {
        public Harness(
            Space storedSpace,
            User currentOwner,
            User newOwner,
            SpaceMembership newOwnerMembership,
            SpaceRole managedAdminRole)
        {
            var session = new FakePersistenceSession();
            session.Seed(currentOwner);
            session.Seed(newOwner);
            UserRepository = new FakeUserRepository(session);
            DetailsReader = new FakeSpaceDetailsReader();
            SpaceRepository = new FakeSpaceRepository();
            MembershipRepository = new FakeSpaceOwnershipMembershipRepository
            {
                CurrentMembership = newOwnerMembership,
                ManagedAdminRole = managedAdminRole,
            };
            UnitOfWork = new FakeSpaceUnitOfWork(SpaceRepository);
            TransactionExecutor = new FakeTransactionExecutor(session);
            Clock = new FakeClock(Now);
            SetStoredSpace(storedSpace, currentOwner.PublicId);
        }

        public FakeSpaceDetailsReader DetailsReader { get; }
        public FakeUserRepository UserRepository { get; }
        public FakeSpaceRepository SpaceRepository { get; }
        public FakeSpaceOwnershipMembershipRepository MembershipRepository { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor TransactionExecutor { get; }
        public FakeClock Clock { get; }

        public TransferSpaceOwnershipUseCase UseCase => new(
            DetailsReader,
            UserRepository,
            SpaceRepository,
            MembershipRepository,
            UnitOfWork,
            TransactionExecutor,
            Clock);

        public void SetStoredSpace(Space space, Guid ownerUserPublicId)
        {
            SpaceRepository.Seed(space);
            DetailsReader.Snapshot = new SpaceDetailsSnapshot(
                space,
                ownerUserPublicId,
                RootFolderPublicId);
        }
    }

    private sealed record TransferScenario(
        Harness Harness,
        Space Space,
        User CurrentOwner,
        User NewOwner,
        SpaceMembership NewOwnerMembership,
        SpaceRole ManagedAdminRole,
        AuthorizedSpaceContext Context,
        TransferSpaceOwnershipCommand Command);

    [Test]
    public async Task ExecuteAsync_ShouldTransferOwnershipAndStageAtomicChanges()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var allChangesStagedBeforeSave = false;
        harness.UnitOfWork.SaveChangesFailureFactory = _ =>
        {
            allChangesStagedBeforeSave = harness.MembershipRepository.DeleteCalls.Count == 1
                && harness.MembershipRepository.AddCalls.Count == 1
                && harness.SpaceRepository.UpdateCalls.Count == 1;
            return null;
        };

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var deletion = harness.MembershipRepository.DeleteCalls.Single();
        var addition = harness.MembershipRepository.AddCalls.Single();
        var update = harness.SpaceRepository.UpdateCalls.Single();
        var assignment = addition.RoleAssignments.Single();
        var dto = result.Value.Space;
        Assert.Multiple(() =>
        {
            Assert.That(deletion.Membership, Is.SameAs(scenario.NewOwnerMembership));
            Assert.That(deletion.ExpectedVersion, Is.EqualTo(scenario.NewOwnerMembership.Version));
            Assert.That(addition.Id, Is.Zero);
            Assert.That(addition.PublicId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(addition.SpaceId, Is.EqualTo(scenario.Space.Id));
            Assert.That(addition.UserId, Is.EqualTo(scenario.CurrentOwner.Id));
            Assert.That(addition.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(addition.Version, Is.EqualTo(1));
            Assert.That(addition.JoinedAt, Is.EqualTo(Now));
            Assert.That(addition.UpdatedAt, Is.EqualTo(Now));
            Assert.That(addition.RoleAssignments, Has.Count.EqualTo(1));
            Assert.That(assignment.RoleId, Is.EqualTo(scenario.ManagedAdminRole.Id));
            Assert.That(assignment.AssignedByUserId, Is.EqualTo(scenario.CurrentOwner.Id));
            Assert.That(assignment.AssignedAt, Is.EqualTo(Now));
            Assert.That(update.Space.OwnerUserId, Is.EqualTo(scenario.NewOwner.Id));
            Assert.That(update.Space.Version, Is.EqualTo(scenario.Space.Version + 1));
            Assert.That(update.Space.UpdatedAt, Is.EqualTo(Now));
            Assert.That(update.ExpectedVersion, Is.EqualTo(scenario.Space.Version));
            Assert.That(allChangesStagedBeforeSave, Is.True);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { scenario.Space.Id }));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { scenario.CurrentOwner.Id }));
            Assert.That(
                harness.UserRepository.SearchedPublicIds,
                Is.EqualTo(new[] { scenario.NewOwner.PublicId }));
            Assert.That(
                harness.MembershipRepository.MembershipLookups,
                Is.EqualTo(new[] { (scenario.Space.Id, scenario.NewOwner.Id) }));
            Assert.That(
                harness.MembershipRepository.ManagedAdminRoleSpaceIds,
                Is.EqualTo(new[] { scenario.Space.Id }));
            Assert.That(dto.OwnerUserPublicId, Is.EqualTo(scenario.NewOwner.PublicId));
            Assert.That(dto.SpacePublicId, Is.EqualTo(scenario.Space.PublicId));
            Assert.That(dto.RootFolderPublicId, Is.EqualTo(RootFolderPublicId));
            Assert.That(dto.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Membership));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(scenario.ManagedAdminRole.Permissions));
            Assert.That(result.Value.Version, Is.EqualTo(scenario.Space.Version + 1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveSystemAdministratorAccess()
    {
        // Arrange
        var scenario = ValidTransfer(ownerSystemRole: SystemRole.SystemAdmin);
        var harness = scenario.Harness;

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                scenario.Context.AuthorizationSource,
                Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
            Assert.That(
                result.Value.Space.MyAccess.Source,
                Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
            Assert.That(
                result.Value.Space.MyAccess.Permissions,
                Is.EquivalentTo(scenario.ManagedAdminRole.Permissions));
            Assert.That(result.Value.Space.OwnerUserPublicId, Is.EqualTo(scenario.NewOwner.PublicId));
            Assert.That(
                harness.SpaceRepository.UpdateCalls.Single().Space.OwnerUserId,
                Is.EqualTo(scenario.NewOwner.Id));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnCurrentSpaceForExistingOwnerWithoutWrites()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var command = new TransferSpaceOwnershipCommand(
            scenario.Context,
            scenario.Space.Version,
            scenario.CurrentOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertNoWrites(harness);
        AssertNoTargetLookups(harness);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Space.SpacePublicId, Is.EqualTo(scenario.Space.PublicId));
            Assert.That(result.Value.Space.OwnerUserPublicId, Is.EqualTo(scenario.CurrentOwner.PublicId));
            Assert.That(result.Value.Space.RootFolderPublicId, Is.EqualTo(RootFolderPublicId));
            Assert.That(result.Value.Space.UpdatedAt, Is.EqualTo(scenario.Space.UpdatedAt));
            Assert.That(result.Value.Space.MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(
                result.Value.Space.MyAccess.Permissions,
                Is.EquivalentTo(scenario.Context.EffectivePermissions));
            Assert.That(result.Value.Version, Is.EqualTo(scenario.Space.Version));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { scenario.CurrentOwner.Id }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleExistingOwnerTransferWithoutWrites()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var command = new TransferSpaceOwnershipCommand(
            scenario.Context,
            scenario.Space.Version - 1,
            scenario.CurrentOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertFailedWrite(harness);
        AssertNoTargetLookups(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMcpActorBeforeLookup()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var context = new AuthorizedSpaceContext(
            scenario.Space.Id,
            scenario.Space.PublicId,
            ActorType.Mcp,
            scenario.CurrentOwner.Id,
            null,
            [SpacePermission.SpaceView],
            SpaceAuthorizationSource.Owner);
        var command = new TransferSpaceOwnershipCommand(
            context,
            scenario.Space.Version,
            scenario.NewOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.TokenTypeNotAllowed);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyFormerOwnerAfterOwnershipTransfer()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var persistedSpace = StoredSpace(SpaceStatus.Active, ownerUserId: 99);
        harness.SetStoredSpace(persistedSpace, OtherOwnerPublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        Assert.That(scenario.Context.UserId, Is.Not.EqualTo(persistedSpace.OwnerUserId));
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertFailedWrite(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyNonOwnerSystemAdministrator()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var administrator = TestSpaceAuthorizationSnapshots.Session(77, SystemRole.SystemAdmin);
        var context = Authorize(scenario.Space, administrator);
        var command = new TransferSpaceOwnershipCommand(
            context,
            scenario.Space.Version,
            scenario.NewOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertFailedWrite(harness);
    }

    [TestCase(UserStatus.Suspended)]
    [TestCase(UserStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectInactiveCurrentOwnerWithoutWrites(UserStatus ownerStatus)
    {
        // Arrange
        var scenario = ValidTransfer(ownerStatus: ownerStatus);
        var harness = scenario.Harness;

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Session.AuthUserInactive);
        AssertFailedWrite(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnNotFoundForMissingSpace()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        harness.DetailsReader.Snapshot = null;

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceNotFound);
        AssertFailedWrite(harness);
        Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { scenario.Space.Id }));
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedStateWithoutWrites(
        SpaceStatus persistedStatus)
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var persistedSpace = StoredSpace(persistedStatus);
        harness.SetStoredSpace(persistedSpace, scenario.CurrentOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                error.Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Auth.SpaceStateNotAllowed));
            Assert.That(error, Is.InstanceOf<SpaceAuthorizationError>());
            Assert.That(((SpaceAuthorizationError)error).ActualSpaceStatus, Is.EqualTo(persistedStatus));
        });
        AssertFailedWrite(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingTargetAccountWithoutWrites()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        harness.UserRepository.FindByPublicIdResultFactory = _ => Result.Ok<User?>(null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Space.TransferTargetInvalid);
        AssertFailedWrite(harness);
    }

    [TestCase(UserStatus.Suspended)]
    [TestCase(UserStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectInactiveTargetAccountWithoutWrites(UserStatus targetStatus)
    {
        // Arrange
        var scenario = ValidTransfer(newOwnerStatus: targetStatus);
        var harness = scenario.Harness;

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Space.TransferTargetInvalid);
        AssertFailedWrite(harness);
    }

    [TestCase(null, SpaceId)]
    [TestCase(SpaceMembershipStatus.Suspended, SpaceId)]
    [TestCase(SpaceMembershipStatus.Left, SpaceId)]
    [TestCase(SpaceMembershipStatus.Active, SpaceId + 1)]
    public async Task ExecuteAsync_ShouldRejectIneligibleTargetMembershipWithoutWrites(
        SpaceMembershipStatus? membershipStatus,
        long membershipSpaceId)
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        harness.MembershipRepository.CurrentMembership = membershipStatus is null
            ? null
            : TargetMembership(membershipSpaceId, scenario.NewOwner.Id, membershipStatus.Value);

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Space.TransferTargetInvalid);
        AssertFailedWrite(harness);
        Assert.That(
            harness.MembershipRepository.MembershipLookups,
            Is.EqualTo(new[] { (scenario.Space.Id, scenario.NewOwner.Id) }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingManagedAdminRoleWithoutWrites()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        harness.MembershipRepository.ManagedAdminRole = null;

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.RoleInvalid);
        AssertFailedWrite(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersionBeforeStagingChanges()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var command = new TransferSpaceOwnershipCommand(
            scenario.Context,
            scenario.Space.Version - 1,
            scenario.NewOwner.PublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertFailedWrite(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateMembershipStagingFailureAndRollback()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        var stagingFailure = SpaceMembershipError.InvalidState("Membership deletion staging failed.");
        harness.MembershipRepository.DeleteResult = Result.Fail(stagingFailure);

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(stagingFailure));
            Assert.That(harness.MembershipRepository.DeleteCalls, Has.Count.EqualTo(1));
            Assert.That(harness.MembershipRepository.AddCalls, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSaveConcurrencyFailureAndRollback()
    {
        // Arrange
        var scenario = ValidTransfer();
        var harness = scenario.Harness;
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();

        // Act
        var result = await harness.UseCase.ExecuteAsync(scenario.Command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        Assert.Multiple(() =>
        {
            Assert.That(harness.MembershipRepository.DeleteCalls, Has.Count.EqualTo(1));
            Assert.That(harness.MembershipRepository.AddCalls, Has.Count.EqualTo(1));
            Assert.That(harness.SpaceRepository.UpdateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireTransferContextWithoutDependencyCalls()
    {
        // Arrange
        var scenario = ValidTransfer();
        var command = new TransferSpaceOwnershipCommand(
            null!,
            scenario.Space.Version,
            scenario.NewOwner.PublicId);

        // Act
        var result = await scenario.Harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.AuthorizationContextRequired,
            nameof(TransferSpaceOwnershipCommand.Space));
        AssertNoDependencyCalls(scenario.Harness);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldRejectNonPositiveVersionWithoutDependencyCalls(long expectedVersion)
    {
        // Arrange
        var scenario = ValidTransfer();
        var command = new TransferSpaceOwnershipCommand(
            scenario.Context,
            expectedVersion,
            scenario.NewOwner.PublicId);

        // Act
        var result = await scenario.Harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(TransferSpaceOwnershipCommand.ExpectedVersion));
        AssertNoDependencyCalls(scenario.Harness);
    }

    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("01977b58-32aa-4c60-a17e-0840c6df1200")]
    [TestCase("01977b58-32aa-7c60-017e-0840c6df1200")]
    public async Task ExecuteAsync_ShouldRejectInvalidTargetPublicIdWithoutDependencyCalls(string publicId)
    {
        // Arrange
        var scenario = ValidTransfer();
        var command = new TransferSpaceOwnershipCommand(
            scenario.Context,
            scenario.Space.Version,
            Guid.Parse(publicId));

        // Act
        var result = await scenario.Harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.TransferTargetInvalid,
            nameof(TransferSpaceOwnershipCommand.NewOwnerUserPublicId));
        AssertNoDependencyCalls(scenario.Harness);
    }

    private static TransferScenario ValidTransfer(
        SystemRole ownerSystemRole = SystemRole.User,
        UserStatus ownerStatus = UserStatus.Active,
        UserStatus newOwnerStatus = UserStatus.Active)
    {
        var space = StoredSpace(SpaceStatus.Active);
        var currentOwner = Account(OwnerUserId, OwnerUserPublicId, ownerStatus, ownerSystemRole);
        var newOwner = Account(NewOwnerUserId, NewOwnerUserPublicId, newOwnerStatus);
        var membership = TargetMembership(space.Id, newOwner.Id);
        var managedAdminRole = ManagedAdminRole(space.Id);
        var actor = TestSpaceAuthorizationSnapshots.Session(currentOwner.Id, ownerSystemRole);
        var context = Authorize(space, actor);
        var harness = new Harness(space, currentOwner, newOwner, membership, managedAdminRole);
        var command = new TransferSpaceOwnershipCommand(context, space.Version, newOwner.PublicId);
        return new TransferScenario(
            harness,
            space,
            currentOwner,
            newOwner,
            membership,
            managedAdminRole,
            context,
            command);
    }

    private static Space StoredSpace(SpaceStatus status, long ownerUserId = OwnerUserId) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId,
            id: SpaceId,
            allowedBytes: 1000,
            usedBytes: status == SpaceStatus.Deleted ? 0 : 400,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : 50,
            version: 3,
            createdAt: Now.AddDays(-7),
            updatedAt: Now.AddDays(-1));

    private static User Account(
        long id,
        Guid publicId,
        UserStatus status = UserStatus.Active,
        SystemRole systemRole = SystemRole.User) =>
        User.Reconstitute(
            id,
            publicId,
            EmailAddress.Create($"user{id}@example.com").Value,
            NormalizedName.Create($"user{id}").Value,
            $"User {id}",
            "hashed-password",
            systemRole,
            status,
            1,
            3,
            Now.AddDays(-30),
            Now.AddDays(-1),
            status == UserStatus.Suspended ? Now.AddDays(-1) : null,
            status == UserStatus.Deleted ? Now.AddDays(-1) : null);

    private static SpaceMembership TargetMembership(
        long spaceId,
        long userId,
        SpaceMembershipStatus status = SpaceMembershipStatus.Active) =>
        SpaceMembership.Reconstitute(
            55,
            MembershipPublicId,
            spaceId,
            userId,
            status,
            6,
            Now.AddDays(-10),
            Now.AddDays(-2),
            status == SpaceMembershipStatus.Suspended ? Now.AddDays(-2) : null,
            status == SpaceMembershipStatus.Left ? Now.AddDays(-2) : null,
            [MembershipRoleAssignment.Create(101, OwnerUserId, Now.AddDays(-10))]);

    private static SpaceRole ManagedAdminRole(long spaceId) =>
        SpaceRole.Reconstitute(
            101,
            AdminRolePublicId,
            spaceId,
            "Admin",
            "ADMIN",
            SpaceRoleSystemKey.Admin,
            null,
            2,
            true,
            4,
            Now.AddDays(-30),
            Now.AddDays(-20),
            null,
            []);

    private static AuthorizedSpaceContext Authorize(Space space, CurrentActor actor)
    {
        var query = TestSpaceAuthorizationSnapshots.Query(actor: actor, spacePublicId: space.PublicId);
        var snapshot = new SpaceAuthorizationSnapshot(space, null, []);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static void AssertFailedWrite(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.UserRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.MembershipRepository.DeleteCalls, Is.Empty);
            Assert.That(harness.MembershipRepository.AddCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        });
    }

    private static void AssertNoTargetLookups(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedPublicIds, Is.Empty);
            Assert.That(harness.MembershipRepository.MembershipLookups, Is.Empty);
            Assert.That(harness.MembershipRepository.ManagedAdminRoleSpaceIds, Is.Empty);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        AssertNoTargetLookups(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.Empty);
            Assert.That(harness.UserRepository.CheckedNormalizedEmails, Is.Empty);
            Assert.That(harness.UserRepository.CheckedNormalizedUserNames, Is.Empty);
            Assert.That(harness.UserRepository.SearchedNormalizedEmails, Is.Empty);
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedPublicIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertValidationError(
        Result<SpaceResultDto> result,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
    }

    private static void AssertError(Result<SpaceResultDto> result, string expectedErrorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(expectedErrorCode));
    }
}
