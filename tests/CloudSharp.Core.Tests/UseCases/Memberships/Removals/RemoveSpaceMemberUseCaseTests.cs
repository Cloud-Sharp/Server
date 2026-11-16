using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Removals;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Removals;

[TestFixture]
public class RemoveSpaceMemberUseCaseTests
{
    private const long SpaceId = 7;
    private const long OtherSpaceId = 8;
    private const long TargetVersion = 4;
    private const string TargetPublicIdText = "01977b5b-164c-73b8-b681-1eabcc05a444";
    private static readonly Guid TargetPublicId = Guid.Parse(TargetPublicIdText);
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task ExecuteAsync_ShouldRemoveMembership(SpaceMembershipStatus status)
    {
        // Arrange
        var target = StoredMembership(status);
        var harness = new Harness(target);
        int? updatesObservedAtSave = null;
        harness.UnitOfWork.SaveChangesFailureFactory = _ =>
        {
            updatesObservedAtSave = harness.Memberships.Updates.Count;
            return null;
        };

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var (updated, expectedVersion) = harness.Memberships.Updates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.EqualTo(new[] { SpaceId }));
            Assert.That(harness.Memberships.Reads, Is.EqualTo(new[] { (SpaceId, TargetPublicId) }));
            Assert.That(expectedVersion, Is.EqualTo(target.Version));
            Assert.That(updated.Id, Is.EqualTo(target.Id));
            Assert.That(updated.PublicId, Is.EqualTo(target.PublicId));
            Assert.That(updated.SpaceId, Is.EqualTo(target.SpaceId));
            Assert.That(updated.UserId, Is.EqualTo(target.UserId));
            Assert.That(updated.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(updated.JoinedAt, Is.EqualTo(target.JoinedAt));
            Assert.That(updated.UpdatedAt, Is.EqualTo(Now));
            Assert.That(updated.LeftAt, Is.EqualTo(Now));
            Assert.That(updated.SuspendedAt, Is.Null);
            Assert.That(updated.RoleAssignments, Is.EquivalentTo(target.RoleAssignments));
            Assert.That(updated.Version, Is.EqualTo(target.Version + 1));
            Assert.That(result.Value.Version, Is.EqualTo(updated.Version));
            Assert.That(updatesObservedAtSave, Is.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectLatestSpaceState(SpaceStatus latestStatus)
    {
        // Arrange
        var harness = new Harness(StoredMembership(), latestStatus);
        var command = ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        var error = result.Errors.Single();
        Assert.That(error, Is.TypeOf<SpaceAuthorizationError>());
        Assert.That(((SpaceAuthorizationError)error).ActualSpaceStatus, Is.EqualTo(latestStatus));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        // Arrange
        var harness = new Harness(StoredMembership(), latestStatus: null);
        var command = ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceNotFound);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectInsufficientPermission()
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        var command = ValidCommand() with { Space = MemberContext(SpacePermission.MembersView) };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertNoPersistenceCalls(harness);
    }

    [TestCase(null, 88L)]
    [TestCase(OtherSpaceId, 88L)]
    [TestCase(SpaceId, 21L)]
    public async Task ExecuteAsync_ShouldReturnMembershipNotFound(long? targetSpaceId, long targetUserId)
    {
        // Arrange
        var target = targetSpaceId is null
            ? null
            : StoredMembership(spaceId: targetSpaceId.Value, userId: targetUserId);
        var harness = new Harness(target);
        var command = ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.NotFound);
        Assert.That(harness.Memberships.Reads, Is.EqualTo(new[] { (SpaceId, TargetPublicId) }));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectLeftMembership()
    {
        // Arrange
        var harness = new Harness(StoredMembership(SpaceMembershipStatus.Left));
        var command = ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.Inactive);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersion()
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        var command = ValidCommand() with { ExpectedVersion = TargetVersion - 1 };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateMembershipReadFailure()
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        harness.Memberships.ReadFailure = CommonError.DependencyUnavailable();

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackRegistrationFailure()
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        harness.Memberships.UpdateFailure = CommonError.DependencyUnavailable();

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackConcurrentSaveConflict()
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        var command = ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.Updates.Single().ExpectedVersion, Is.EqualTo(TargetVersion));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [TestCase("00000000-0000-0000-0000-000000000000", TargetVersion,
        ErrorCodes.SpaceMembership.PublicIdInvalid, nameof(RemoveSpaceMemberCommand.MembershipPublicId))]
    [TestCase(TargetPublicIdText, 0L,
        ErrorCodes.Space.ExpectedVersionInvalid, nameof(RemoveSpaceMemberCommand.ExpectedVersion))]
    public async Task ExecuteAsync_ShouldRejectMalformedCommand(
        string membershipPublicId,
        long expectedVersion,
        string errorCode,
        string propertyName)
    {
        // Arrange
        var harness = new Harness(StoredMembership());
        var command = new RemoveSpaceMemberCommand(MemberContext(), Guid.Parse(membershipPublicId), expectedVersion);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
        AssertNoPersistenceCalls(harness);
    }

    private static RemoveSpaceMemberCommand ValidCommand() =>
        new(MemberContext(), TargetPublicId, TargetVersion);

    private static Space StoredSpace(SpaceStatus status) =>
        TestSpaceAuthorizationSnapshots.Space(status, id: SpaceId, version: 9);

    private static AuthorizedSpaceContext MemberContext(SpacePermission permission = SpacePermission.MembersManage)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var space = StoredSpace(SpaceStatus.Active);
        var membership = TestSpaceAuthorizationSnapshots.Membership(userId: actor.UserId, spaceId: space.Id);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: space.Id, permissions: [permission]);
        var query = TestSpaceAuthorizationSnapshots.Query(actor, [permission], [SpaceStatus.Active], space.PublicId);

        return SessionSpaceAuthorizationPolicy.Evaluate(
            query, new SpaceAuthorizationSnapshot(space, membership, [role])).Value;
    }

    private static SpaceMembership StoredMembership(
        SpaceMembershipStatus status = SpaceMembershipStatus.Active,
        long spaceId = SpaceId,
        long userId = 88)
    {
        var joinedAt = Now.AddDays(-7);
        var updatedAt = Now.AddDays(-1);
        return SpaceMembership.Reconstitute(
            101, TargetPublicId, spaceId, userId, status, TargetVersion, joinedAt, updatedAt,
            status == SpaceMembershipStatus.Suspended ? updatedAt : null,
            status == SpaceMembershipStatus.Left ? updatedAt : null,
            [MembershipRoleAssignment.Create(101, 21, joinedAt)]);
    }

    private static void AssertError(Result<RemoveSpaceMemberResultDto> result, string expectedErrorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.Updates, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        });
    }

    private static void AssertNoPersistenceCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Spaces.SearchedPublicIds, Is.Empty);
            Assert.That(harness.Memberships.Reads, Is.Empty);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    private sealed class Harness
    {
        public Harness(SpaceMembership? target, SpaceStatus? latestStatus = SpaceStatus.Active)
        {
            if (latestStatus is { } status)
            {
                Spaces.Seed(StoredSpace(status));
            }

            Memberships = new MembershipRepository(target);
            UseCase = new RemoveSpaceMemberUseCase(Spaces, Memberships, UnitOfWork, Transaction, new FakeClock(Now));
        }

        public FakeSpaceRepository Spaces { get; } = new();
        public MembershipRepository Memberships { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; } = new();
        public FakeTransactionExecutor Transaction { get; } = new();
        public RemoveSpaceMemberUseCase UseCase { get; }
    }

    private sealed class MembershipRepository(SpaceMembership? target) : ISpaceMembershipRepository
    {
        private readonly List<(long SpaceId, Guid MembershipPublicId)> reads = new();
        private readonly List<(SpaceMembership Membership, long ExpectedVersion)> updates = new();

        public IReadOnlyList<(long SpaceId, Guid MembershipPublicId)> Reads => reads;
        public IReadOnlyList<(SpaceMembership Membership, long ExpectedVersion)> Updates => updates;
        public IError? ReadFailure { get; set; }
        public IError? UpdateFailure { get; set; }

        public Task<Result<SpaceMembership?>> FindByPublicIdAsync(
            long spaceId, Guid membershipPublicId, CancellationToken cancellationToken = default)
        {
            reads.Add((spaceId, membershipPublicId));
            if (ReadFailure is { } failure)
            {
                return Task.FromResult(Result.Fail<SpaceMembership?>(failure));
            }

            var found = target is not null && target.SpaceId == spaceId && target.PublicId == membershipPublicId
                ? Copy(target)
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
            long spaceId, long userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Admin removal looks up the selected membership by public ID.");

        public Task<Result> AddAsync(
            SpaceMembership membership, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Admin removal does not add memberships.");

        public Task<Result> UpdateAsync(
            SpaceMembership membership, long expectedVersion, CancellationToken cancellationToken = default)
        {
            updates.Add((Copy(membership), expectedVersion));
            return Task.FromResult(UpdateFailure is { } failure ? Result.Fail(failure) : Result.Ok());
        }

        private static SpaceMembership Copy(SpaceMembership membership) =>
            SpaceMembership.Reconstitute(
                membership.Id, membership.PublicId, membership.SpaceId, membership.UserId,
                membership.Status, membership.Version, membership.JoinedAt, membership.UpdatedAt,
                membership.SuspendedAt, membership.LeftAt, membership.RoleAssignments);
    }
}
