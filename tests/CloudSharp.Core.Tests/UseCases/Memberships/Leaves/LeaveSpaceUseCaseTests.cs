using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Leaves;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Leaves;

[TestFixture]
public class LeaveSpaceUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid MembershipPublicId = Guid.Parse("01977b5b-164c-73b8-b681-1eabcc05a444");
    private static readonly Guid SpacePublicId = TestSpaceAuthorizationSnapshots.SpacePublicId;
    private static readonly CurrentActor Actor = TestSpaceAuthorizationSnapshots.Session();

    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Suspended)]
    public async Task ExecuteAsync_ShouldRecordMembershipDeparture(
        SpaceStatus spaceStatus,
        SpaceMembershipStatus membershipStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(spaceStatus);
        var membership = Membership(space.Id, Actor.UserId, membershipStatus);
        var harness = new Harness(space, membership);
        var stagedBeforeSave = false;
        harness.UnitOfWork.BeforeSaveChanges = _ => stagedBeforeSave = harness.Memberships.Updates.Count == 1;

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.Spaces.SearchedPublicIds, Is.EqualTo(new[] { space.PublicId }));
        Assert.That(harness.Memberships.UserLookups, Is.EqualTo(new[] { (space.Id, Actor.UserId) }));
        Assert.That(harness.Memberships.Updates, Has.Count.EqualTo(1));
        var update = harness.Memberships.Updates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.ExpectedVersion, Is.EqualTo(membership.Version));
            Assert.That(update.Membership.PublicId, Is.EqualTo(membership.PublicId));
            Assert.That(update.Membership.SpaceId, Is.EqualTo(space.Id));
            Assert.That(update.Membership.UserId, Is.EqualTo(Actor.UserId));
            Assert.That(update.Membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(update.Membership.LeftAt, Is.EqualTo(Now));
            Assert.That(update.Membership.SuspendedAt, Is.Null);
            Assert.That(update.Membership.UpdatedAt, Is.EqualTo(Now));
            Assert.That(update.Membership.JoinedAt, Is.EqualTo(membership.JoinedAt));
            Assert.That(update.Membership.Version, Is.EqualTo(membership.Version + 1));
            Assert.That(update.Membership.RoleAssignments, Is.EquivalentTo(membership.RoleAssignments));
            Assert.That(stagedBeforeSave, Is.True);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        }
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldLeaveExistingDepartureUnchanged(SpaceStatus spaceStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(spaceStatus);
        var membership = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Left);
        var harness = new Harness(space, membership);

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.Memberships.ReadMembership, Is.Not.Null);
        var observed = harness.Memberships.ReadMembership!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Memberships.UserLookups, Is.EqualTo(new[] { (space.Id, Actor.UserId) }));
            Assert.That(observed.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(observed.Version, Is.EqualTo(membership.Version));
            Assert.That(observed.UpdatedAt, Is.EqualTo(membership.UpdatedAt));
            Assert.That(observed.LeftAt, Is.EqualTo(membership.LeftAt));
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        }

        AssertNoWrites(harness);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldRequireOwnershipTransfer(SpaceStatus spaceStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(spaceStatus, ownerUserId: Actor.UserId);
        var harness = new Harness(space);

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertNoWrites(harness);
    }

    [TestCase(7L, 42L, false)]
    [TestCase(8L, 42L, true)]
    [TestCase(7L, 43L, true)]
    public async Task ExecuteAsync_ShouldReturnMemberNotFound(
        long membershipSpaceId,
        long membershipUserId,
        bool hasMembership)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var membership = hasMembership
            ? Membership(membershipSpaceId, membershipUserId, SpaceMembershipStatus.Left)
            : null;
        var harness = new Harness(space, membership);

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        AssertError(result, ErrorCodes.SpaceMembership.NotFound);
        Assert.That(harness.Memberships.UserLookups, Is.EqualTo(new[] { (space.Id, Actor.UserId) }));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        var harness = new Harness();

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, SpacePublicId));

        AssertError(result, ErrorCodes.Auth.SpaceNotFound);
        Assert.That(harness.Memberships.UserLookups, Is.Empty);
        AssertNoWrites(harness);
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldReturnSpaceStateNotAllowed(SpaceStatus spaceStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(spaceStatus);
        var harness = new Harness(space, Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Left));

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(result.Errors.Single(), Is.TypeOf<SpaceAuthorizationError>()
            .With.Property(nameof(SpaceAuthorizationError.ActualSpaceStatus)).EqualTo(spaceStatus));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollBackFailedSave()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var membership = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Active);
        var harness = new Harness(space, membership);
        var failure = CommonError.DependencyUnavailable();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsFailed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(harness.Memberships.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldConfirmConcurrentDepartureAfterRollback()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var original = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Active);
        var departed = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Left);
        var harness = new Harness(space, original);
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        harness.Memberships.ExactRead = () =>
        {
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
            return Result.Ok<SpaceMembership?>(departed);
        };

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsSuccess, Is.True);
        AssertOriginalMembershipRechecked(harness, original);
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    [TestCase(null)]
    public async Task ExecuteAsync_ShouldReturnRequestConflict(SpaceMembershipStatus? currentStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var original = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Active);
        var harness = new Harness(space, original);
        var current = currentStatus is { } status ? Membership(space.Id, Actor.UserId, status) : null;
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        harness.Memberships.ExactRead = () => Result.Ok(current);

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        AssertError(result, ErrorCodes.Common.RequestConflict);
        AssertOriginalMembershipRechecked(harness, original);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateMembershipRecheckFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var original = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Active);
        var harness = new Harness(space, original);
        var failure = CommonError.DependencyUnavailable();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        harness.Memberships.ExactRead = () => Result.Fail<SpaceMembership?>(failure);

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        AssertOriginalMembershipRechecked(harness, original);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateTransactionCommitFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var original = Membership(space.Id, Actor.UserId, SpaceMembershipStatus.Active);
        var harness = new Harness(space, original);
        var failure = CommonError.PreconditionFailed();
        harness.Transactions.CommitFailure = failure;

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, space.PublicId));

        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        Assert.That(harness.Memberships.PublicIdLookups, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectInvalidSpacePublicIdWithoutDependencyCalls()
    {
        var harness = new Harness();

        var result = await harness.UseCase.ExecuteAsync(new LeaveSpaceCommand(Actor, Guid.Empty));

        AssertError(result, ErrorCodes.Space.PublicIdInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(nameof(LeaveSpaceCommand.SpacePublicId)));
            Assert.That(harness.Spaces.SearchedPublicIds, Is.Empty);
            Assert.That(harness.Memberships.UserLookups, Is.Empty);
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        }

        AssertNoWrites(harness);
    }

    private static SpaceMembership Membership(long spaceId, long userId, SpaceMembershipStatus status)
    {
        var joinedAt = Now.AddDays(-7);
        var updatedAt = Now.AddDays(-1);
        return SpaceMembership.Reconstitute(
            55, MembershipPublicId, spaceId, userId, status, 6, joinedAt, updatedAt,
            status == SpaceMembershipStatus.Suspended ? updatedAt : null,
            status == SpaceMembershipStatus.Left ? updatedAt : null,
            [MembershipRoleAssignment.Create(101, 21, joinedAt)]);
    }

    private static void AssertError(Result<LeaveSpaceResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private static void AssertNoWrites(Harness harness)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Memberships.Updates, Is.Empty);
            Assert.That(harness.Spaces.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        }
    }

    private static void AssertOriginalMembershipRechecked(Harness harness, SpaceMembership original)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Memberships.UserLookups, Is.EqualTo(new[] { (original.SpaceId, original.UserId) }));
            Assert.That(harness.Memberships.PublicIdLookups, Is.EqualTo(new[] { (original.SpaceId, original.PublicId) }));
            Assert.That(harness.Memberships.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
        }
    }

    private sealed class Harness
    {
        public Harness(Space? space = null, SpaceMembership? membership = null)
        {
            if (space is not null)
            {
                Spaces.Seed(space);
            }

            var session = new FakePersistenceSession();
            Memberships = new MembershipRepository(membership);
            UnitOfWork = new FakeUnitOfWork(session);
            Transactions = new FakeTransactionExecutor(session);
            UseCase = new LeaveSpaceUseCase(Spaces, Memberships, UnitOfWork, Transactions, new FakeClock(Now));
        }

        public FakeSpaceRepository Spaces { get; } = new();
        public MembershipRepository Memberships { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor Transactions { get; }
        public LeaveSpaceUseCase UseCase { get; }
    }

    private sealed class MembershipRepository(SpaceMembership? membership) : ISpaceMembershipRepository
    {
        public List<(long SpaceId, long UserId)> UserLookups { get; } = [];
        public List<(long SpaceId, Guid PublicId)> PublicIdLookups { get; } = [];
        public List<(SpaceMembership Membership, long ExpectedVersion)> Updates { get; } = [];
        public SpaceMembership? ReadMembership { get; private set; }
        public Func<Result<SpaceMembership?>>? ExactRead { get; set; }

        public Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
            long spaceId,
            long userId,
            CancellationToken cancellationToken = default)
        {
            UserLookups.Add((spaceId, userId));
            ReadMembership = membership is not null
                ? SpaceMembership.Reconstitute(
                    membership.Id, membership.PublicId, membership.SpaceId, membership.UserId,
                    membership.Status, membership.Version, membership.JoinedAt, membership.UpdatedAt,
                    membership.SuspendedAt, membership.LeftAt, membership.RoleAssignments)
                : null;
            return Task.FromResult(Result.Ok(ReadMembership));
        }

        public Task<Result<SpaceMembership?>> FindByPublicIdAsync(
            long spaceId,
            Guid membershipPublicId,
            CancellationToken cancellationToken = default)
        {
            PublicIdLookups.Add((spaceId, membershipPublicId));
            var exactRead = ExactRead ?? throw new InvalidOperationException("Unexpected Membership recheck.");
            return Task.FromResult(exactRead());
        }

        public Task<Result> AddAsync(
            SpaceMembership membership, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Self leave does not add memberships.");

        public Task<Result> UpdateAsync(
            SpaceMembership membership,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((membership, expectedVersion));
            return Task.FromResult(Result.Ok());
        }
    }
}
