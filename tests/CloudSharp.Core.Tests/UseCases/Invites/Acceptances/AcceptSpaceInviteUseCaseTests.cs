using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Invites.Acceptances;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Invites.Acceptances;

[TestFixture]
public sealed class AcceptSpaceInviteUseCaseTests
{
    private const long UserId = 42;
    private const long SpaceId = 7;
    private const long ViewerRoleId = 101;
    private static readonly DateTimeOffset Now = TestSpaceInvites.Now;

    [TestCase(null, null)]
    [TestCase(2, null)]
    [TestCase(2, SpaceMembershipStatus.Left)]
    public async Task ExecuteAsync_ShouldCreateMembershipAndRecordAcceptance(
        int? maxUses,
        SpaceMembershipStatus? existingStatus)
    {
        var invite = StoredInvite(maxUses: maxUses);
        var previous = existingStatus is { } status ? StoredMembership(status) : null;
        var previousSnapshot = previous is null ? null : TestSpaceInvites.Copy(previous);
        var harness = ValidHarness(invite, previous);
        int? membershipAddsAtSave = null;
        int? inviteUpdatesAtSave = null;
        harness.UnitOfWork.SaveChangesFailureFactory = _ =>
        {
            membershipAddsAtSave = harness.Memberships.AddedMemberships.Count;
            inviteUpdatesAtSave = harness.Invites.Updates.Count;
            return null;
        };

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsSuccess, Is.True);
        var membership = harness.Memberships.AddedMemberships.Single();
        var update = harness.Invites.Updates.Single();
        var assignment = membership.RoleAssignments.Single();
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenHasher.HashedTokens, Is.EqualTo(new[] { TestSpaceInvites.PlainToken }));
            Assert.That(harness.Invites.TokenHashReads, Is.EqualTo(new[] { TestSpaceInvites.Hash() }));
            Assert.That(harness.Users.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.InviteTargets.LockedSpaceIds, Is.EqualTo(new[] { SpaceId }));
            Assert.That(harness.Memberships.UserLookups, Is.EqualTo(new[] { (SpaceId, UserId) }));
            Assert.That(harness.Roles.ReadSpaceIds, Is.EqualTo(new[] { SpaceId }));
            Assert.That(membership.Id, Is.Zero);
            Assert.That(membership.PublicId.Version, Is.EqualTo(7));
            Assert.That(membership.PublicId.Variant, Is.InRange(8, 11));
            Assert.That(membership.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(membership.UserId, Is.EqualTo(UserId));
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(membership.Version, Is.EqualTo(1));
            Assert.That(membership.JoinedAt, Is.EqualTo(Now));
            Assert.That(membership.UpdatedAt, Is.EqualTo(Now));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.LeftAt, Is.Null);
            Assert.That(assignment.RoleId, Is.EqualTo(ViewerRoleId));
            Assert.That(assignment.AssignedAt, Is.EqualTo(Now));
            Assert.That(update.ExpectedVersion, Is.EqualTo(invite.Version));
            Assert.That(update.Invite.UseCount, Is.EqualTo(invite.UseCount + 1));
            Assert.That(update.Invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(update.Invite.Version, Is.EqualTo(invite.Version + 1));
            Assert.That(update.Invite.UpdatedAt, Is.EqualTo(Now));
            Assert.That(update.Invite.TokenHash, Is.EqualTo(invite.TokenHash));
            Assert.That(result.Value.SpacePublicId, Is.EqualTo(TestSpaceAuthorizationSnapshots.SpacePublicId));
            Assert.That(result.Value.MembershipPublicId, Is.EqualTo(membership.PublicId));
            Assert.That(result.Value.AlreadyMember, Is.False);
            Assert.That(membershipAddsAtSave, Is.EqualTo(1));
            Assert.That(inviteUpdatesAtSave, Is.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
        if (previousSnapshot is not null)
        {
            Assert.Multiple(() =>
            {
                Assert.That(previous!.PublicId, Is.Not.EqualTo(membership.PublicId));
                Assert.That(previous.Status, Is.EqualTo(previousSnapshot.Status));
                Assert.That(previous.Version, Is.EqualTo(previousSnapshot.Version));
                Assert.That(previous.RoleAssignments, Is.EquivalentTo(previousSnapshot.RoleAssignments));
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldReturnAlreadyMemberWithoutWrites(bool owner)
    {
        var invite = StoredInvite();
        var existing = owner ? null : StoredMembership(SpaceMembershipStatus.Active);
        var harness = ValidHarness(invite, existing, ownerUserId: owner ? UserId : 21);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.SpacePublicId, Is.EqualTo(TestSpaceAuthorizationSnapshots.SpacePublicId));
            Assert.That(result.Value.MembershipPublicId, Is.EqualTo(owner ? null : existing!.PublicId));
            Assert.That(result.Value.AlreadyMember, Is.True);
            Assert.That(invite.UseCount, Is.Zero);
            Assert.That(invite.Version, Is.EqualTo(3));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now.AddDays(-1)));
            AssertNoWrites(harness);
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
        if (existing is not null)
        {
            Assert.Multiple(() =>
            {
                Assert.That(existing.Status, Is.EqualTo(SpaceMembershipStatus.Active));
                Assert.That(existing.Version, Is.EqualTo(5));
                Assert.That(existing.RoleAssignments.Single().RoleId, Is.EqualTo(ViewerRoleId));
            });
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldUseOwnerFromLockedRead()
    {
        var harness = ValidHarness(StoredInvite(), StoredMembership(SpaceMembershipStatus.Active));
        harness.InviteTargets.BeforeLockedRead = () =>
        {
            harness.InviteTargets.Replace(StoredSpace(SpaceStatus.Active, UserId));
            harness.Memberships.Current = null;
        };

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.AlreadyMember, Is.True);
        Assert.That(result.Value.MembershipPublicId, Is.Null);
        Assert.That(harness.Memberships.UserLookups, Is.Empty);
        AssertNoWrites(harness);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_ShouldRejectUnusableInvite(bool owner)
    {
        var invite = StoredInvite(status: SpaceInviteStatus.Revoked);
        var existing = owner ? null : StoredMembership(SpaceMembershipStatus.Active);
        var harness = ValidHarness(invite, existing, ownerUserId: owner ? UserId : 21);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.SpaceInvite.Revoked);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.UserLookups, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldExpireInviteAtLastUse()
    {
        var invite = StoredInvite(maxUses: 1);
        var harness = ValidHarness(invite);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsSuccess, Is.True);
        var update = harness.Invites.Updates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.Invite.UseCount, Is.EqualTo(1));
            Assert.That(update.Invite.Status, Is.EqualTo(SpaceInviteStatus.Expired));
            Assert.That(update.Invite.Version, Is.EqualTo(invite.Version + 1));
            Assert.That(update.Invite.UpdatedAt, Is.EqualTo(Now));
            Assert.That(update.ExpectedVersion, Is.EqualTo(invite.Version));
            Assert.That(harness.Memberships.AddedMemberships, Has.Count.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.EqualTo(1));
        });
    }

    [TestCase(SpaceInviteStatus.Revoked, 1, 1, 0, ErrorCodes.SpaceInvite.Revoked)]
    [TestCase(SpaceInviteStatus.Active, 1, 1, 0, ErrorCodes.SpaceInvite.UsageExhausted)]
    [TestCase(SpaceInviteStatus.Expired, 1, 1, 3600, ErrorCodes.SpaceInvite.UsageExhausted)]
    [TestCase(SpaceInviteStatus.Active, 0, 1, 0, ErrorCodes.SpaceInvite.Expired)]
    public async Task ExecuteAsync_ShouldApplyInviteFailurePrecedence(
        SpaceInviteStatus status,
        int useCount,
        int maxUses,
        int expirySecondsFromNow,
        string errorCode)
    {
        var invite = StoredInvite(
            status: status,
            useCount: useCount,
            maxUses: maxUses,
            expiresAt: Now.AddSeconds(expirySecondsFromNow));
        var harness = ValidHarness(invite);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, errorCode);
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(status));
            Assert.That(invite.UseCount, Is.EqualTo(useCount));
            Assert.That(invite.Version, Is.EqualTo(3));
        });
    }

    [TestCase(UserStatus.Suspended)]
    [TestCase(UserStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectInactiveLatestUser(UserStatus status)
    {
        var harness = ValidHarness(StoredInvite(), userStatus: status);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
        Assert.That(harness.Users.SearchedIds, Is.EqualTo(new[] { UserId }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingLatestUser()
    {
        var invite = StoredInvite();
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(invite, space, null);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectUnavailableLatestSpace(SpaceStatus status)
    {
        var harness = ValidHarness(StoredInvite());
        harness.InviteTargets.BeforeLockedRead = () => harness.InviteTargets.Replace(StoredSpace(status));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        AssertNoWrites(harness);
        Assert.That(harness.InviteTargets.LockedSpaceIds, Is.EqualTo(new[] { SpaceId }));
        Assert.That(harness.Memberships.UserLookups, Is.Empty);
        Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingSpace()
    {
        var invite = StoredInvite();
        var harness = new Harness(invite, null, StoredUser(Actor()));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectUnknownToken()
    {
        var harness = new Harness(null, StoredSpace(SpaceStatus.Active), StoredUser(Actor()));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Users.SearchedIds, Is.Empty);
            Assert.That(harness.Memberships.UserLookups, Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ExecuteAsync_ShouldRejectMissingOrCrossSpaceViewer(int roleScenario)
    {
        SpaceRole[] roles = roleScenario switch
        {
            0 => [],
            1 => [TestSpaceInvites.Role(spaceId: 8)],
            _ => [TestSpaceInvites.Role(deletedAt: Now.AddDays(-1))],
        };
        var harness = ValidHarness(StoredInvite(), roles: roles);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectSuspendedMembership()
    {
        var existing = StoredMembership(SpaceMembershipStatus.Suspended);
        var snapshot = TestSpaceInvites.Copy(existing);
        var harness = ValidHarness(StoredInvite(), existing);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(existing.Status, Is.EqualTo(snapshot.Status));
            Assert.That(existing.Version, Is.EqualTo(snapshot.Version));
            Assert.That(existing.RoleAssignments, Is.EquivalentTo(snapshot.RoleAssignments));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackMembershipAddFailure()
    {
        var harness = ValidHarness(StoredInvite());
        harness.Memberships.AddFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.AddedMemberships, Has.Count.EqualTo(1));
            Assert.That(harness.Invites.Updates, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackInviteUpdateFailure()
    {
        var harness = ValidHarness(StoredInvite());
        harness.Invites.UpdateFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.AddedMemberships, Has.Count.EqualTo(1));
            Assert.That(harness.Invites.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveFailure()
    {
        var harness = ValidHarness(StoredInvite());
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.AddedMemberships, Has.Count.EqualTo(1));
            Assert.That(harness.Invites.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackCommitFailure()
    {
        var harness = ValidHarness(StoredInvite());
        harness.Transaction.CommitFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Memberships.AddedMemberships, Has.Count.EqualTo(1));
            Assert.That(harness.Invites.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteAsync_ShouldThrowArgumentNullException()
    {
        var harness = ValidHarness(StoredInvite());

        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireActor()
    {
        var harness = ValidHarness(StoredInvite());

        var result = await harness.UseCase.ExecuteAsync(
            new AcceptSpaceInviteCommand(null!, TestSpaceInvites.PlainToken));

        AssertValidationError(result, ErrorCodes.Space.ActorRequired, nameof(AcceptSpaceInviteCommand.Actor));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMalformedSessionActor()
    {
        var harness = ValidHarness(StoredInvite());
        var actor = CurrentActor.ForSession(0, Guid.Empty, (SystemRole)999, Guid.Empty);

        var result = await harness.UseCase.ExecuteAsync(
            new AcceptSpaceInviteCommand(actor, TestSpaceInvites.PlainToken));

        AssertError(result, ErrorCodes.Auth.TokenInvalid);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMcpActor()
    {
        var harness = ValidHarness(StoredInvite());
        var actor = CurrentActor.ForMcp(
            UserId, TestSpaceInvites.PublicId(42), SystemRole.User, 900, TestSpaceInvites.PublicId(900));

        var result = await harness.UseCase.ExecuteAsync(
            new AcceptSpaceInviteCommand(actor, TestSpaceInvites.PlainToken));

        AssertError(result, ErrorCodes.Auth.TokenTypeNotAllowed);
        AssertNoDependencyCalls(harness);
    }

    [TestCaseSource(nameof(InvalidTokens))]
    public async Task ExecuteAsync_ShouldRejectInvalidToken(string? token)
    {
        var harness = ValidHarness(StoredInvite());

        var result = await harness.UseCase.ExecuteAsync(new AcceptSpaceInviteCommand(Actor(), token!));

        AssertValidationError(result, ErrorCodes.SpaceInvite.TokenInvalid, nameof(AcceptSpaceInviteCommand.InviteToken));
        AssertNoDependencyCalls(harness);
    }

    private static IEnumerable<TestCaseData> InvalidTokens()
    {
        yield return new TestCaseData(null).SetName("Token is null");
        yield return new TestCaseData("").SetName("Token is empty");
        yield return new TestCaseData(" ").SetName("Token is whitespace");
        yield return new TestCaseData("cs_sess_" + new string('A', 43)).SetName("Token prefix is wrong");
        yield return new TestCaseData("cs_inv_short").SetName("Token length is wrong");
        yield return new TestCaseData("cs_inv_" + new string('!', 43)).SetName("Token body is not base64url");
        yield return new TestCaseData("cs_inv_" + new string('A', 44)).SetName("Token decoded length is wrong");
    }

    private static AcceptSpaceInviteCommand ValidCommand() => new(Actor(), TestSpaceInvites.PlainToken);

    private static CurrentActor Actor(long userId = UserId) =>
        CurrentActor.ForSession(
            userId,
            TestSpaceInvites.PublicId((int)userId),
            SystemRole.User,
            TestSpaceInvites.PublicId(10_000 + (int)userId));

    private static SpaceInvite StoredInvite(
        SpaceInviteStatus status = SpaceInviteStatus.Active,
        int useCount = 0,
        int? maxUses = 2,
        DateTimeOffset? expiresAt = null) =>
        TestSpaceInvites.Stored(
            spaceId: SpaceId,
            status: status,
            useCount: useCount,
            maxUses: maxUses,
            expiresAt: expiresAt);

    private static Space StoredSpace(SpaceStatus status, long ownerUserId = 21) =>
        TestSpaceAuthorizationSnapshots.Space(status, ownerUserId: ownerUserId, id: SpaceId, version: 4);

    private static User StoredUser(CurrentActor actor, UserStatus status = UserStatus.Active)
    {
        var updatedAt = Now.AddDays(-2);
        return User.Reconstitute(
            actor.UserId,
            actor.UserPublicId,
            EmailAddress.Create($"user-{actor.UserId}@example.com").Value,
            null,
            "Invite user",
            "v1:argon2id$abc$def$hash",
            actor.SystemRole,
            status,
            1,
            4,
            Now.AddDays(-30),
            updatedAt,
            status == UserStatus.Suspended ? updatedAt : null,
            status == UserStatus.Deleted ? updatedAt : null);
    }

    private static SpaceMembership StoredMembership(SpaceMembershipStatus status)
    {
        var joinedAt = Now.AddDays(-10);
        var updatedAt = Now.AddDays(-2);
        return SpaceMembership.Reconstitute(
            200,
            TestSpaceInvites.PublicId(200),
            SpaceId,
            UserId,
            status,
            5,
            joinedAt,
            updatedAt,
            status == SpaceMembershipStatus.Suspended ? updatedAt : null,
            status == SpaceMembershipStatus.Left ? updatedAt : null,
            [MembershipRoleAssignment.Create(ViewerRoleId, 21, joinedAt)]);
    }

    private static Harness ValidHarness(
        SpaceInvite invite,
        SpaceMembership? membership = null,
        UserStatus userStatus = UserStatus.Active,
        long ownerUserId = 21,
        IEnumerable<SpaceRole>? roles = null) =>
        new(invite, StoredSpace(SpaceStatus.Active, ownerUserId), StoredUser(Actor(), userStatus), membership, roles);

    private static void AssertError(Result<AcceptSpaceInviteResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<AcceptSpaceInviteResultDto> result, string errorCode, string propertyName)
    {
        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Is.Empty);
            Assert.That(harness.Invites.Updates, Is.Empty);
            Assert.That(harness.Memberships.AddedMemberships, Is.Empty);
            Assert.That(harness.Memberships.Updates, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenHasher.HashedTokens, Is.Empty);
            Assert.That(harness.Invites.TokenHashReads, Is.Empty);
            Assert.That(harness.Users.SearchedIds, Is.Empty);
            Assert.That(harness.InviteTargets.LockedSpaceIds, Is.Empty);
            Assert.That(harness.Memberships.UserLookups, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    private sealed class Harness
    {
        public Harness(
            SpaceInvite? invite,
            Space? space,
            User? user,
            SpaceMembership? membership = null,
            IEnumerable<SpaceRole>? roles = null)
        {
            InviteTargets.Replace(space);
            Users.FoundByIdUser = user;
            Invites = new InviteRepository(invite);
            Memberships = new MembershipRepository(membership);
            Roles = new RoleReader(roles ?? DefaultRoles());
            UseCase = new AcceptSpaceInviteUseCase(
                TokenHasher, InviteTargets, Users, UnitOfWork, Transaction, new FakeClock(Now),
                Invites, Roles, Memberships);
        }

        public TokenHasher TokenHasher { get; } = new();
        public InviteRepository Invites { get; }
        public InviteTargetReader InviteTargets { get; } = new();
        public RoleReader Roles { get; }
        public FakeUserRepository Users { get; } = new();
        public MembershipRepository Memberships { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; } = new();
        public FakeTransactionExecutor Transaction { get; } = new();
        public AcceptSpaceInviteUseCase UseCase { get; }

        private static IReadOnlyList<SpaceRole> DefaultRoles() =>
        [
            TestSpaceInvites.Role(),
            TestSpaceInvites.Role(id: 102, systemKey: SpaceRoleSystemKey.Member, name: "Editor"),
            TestSpaceInvites.Role(id: 103, spaceId: 8, name: "Other viewer"),
        ];
    }

    private sealed class InviteTargetReader : ISpaceInviteTargetReader
    {
        private SpaceInviteTargetSnapshot? snapshot;

        public List<long> LockedSpaceIds { get; } = [];
        public Action? BeforeLockedRead { get; set; }

        public void Replace(Space? space) => snapshot = space is null
            ? null
            : new SpaceInviteTargetSnapshot(space.Id, space.PublicId, space.OwnerUserId, space.Status);

        public Task<Result<SpaceInviteTargetSnapshot?>> ReadLockedAsync(
            long spaceId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LockedSpaceIds.Add(spaceId);
            BeforeLockedRead?.Invoke();
            return Task.FromResult(Result.Ok(snapshot?.Id == spaceId ? snapshot : null));
        }
    }

    private sealed class TokenHasher : ITokenHasher
    {
        public List<string> HashedTokens { get; } = [];

        public string Hash(string plainToken)
        {
            HashedTokens.Add(plainToken);
            return TestSpaceInvites.TokenHashValue;
        }

        public bool Verify(string plainToken, string expectedHashedToken) =>
            throw new InvalidOperationException("Invite acceptance hashes the full token before repository lookup.");
    }

    private sealed record InviteUpdate(SpaceInvite Invite, long ExpectedVersion);

    private sealed class InviteRepository(SpaceInvite? target) : ISpaceInviteRepository
    {
        public List<TokenHash> TokenHashReads { get; } = [];
        public List<SpaceInvite> AddedInvites { get; } = [];
        public List<InviteUpdate> Updates { get; } = [];
        public IError? UpdateFailure { get; set; }

        public Task<Result<SpaceInvite?>> FindByTokenHashAsync(
            TokenHash tokenHash, CancellationToken cancellationToken = default)
        {
            TokenHashReads.Add(tokenHash);
            var found = target is not null && target.TokenHash == tokenHash
                ? TestSpaceInvites.Copy(target)
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result> AddAsync(SpaceInvite invite, CancellationToken cancellationToken = default)
        {
            AddedInvites.Add(TestSpaceInvites.Copy(invite));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> UpdateAsync(
            SpaceInvite invite, long expectedVersion, CancellationToken cancellationToken = default)
        {
            Updates.Add(new InviteUpdate(TestSpaceInvites.Copy(invite), expectedVersion));
            return Task.FromResult(UpdateFailure is not null ? Result.Fail(UpdateFailure) : Result.Ok());
        }

        public Task<Result<SpaceInvite?>> FindByPublicIdAsync(
            long spaceId, Guid invitePublicId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invite acceptance selects an invite by token hash.");
    }

    private sealed record MembershipUpdate(SpaceMembership Membership, long ExpectedVersion);

    private sealed class MembershipRepository(SpaceMembership? target) : ISpaceMembershipRepository
    {
        public SpaceMembership? Current { get; set; } = target;
        public List<(long SpaceId, long UserId)> UserLookups { get; } = [];
        public List<SpaceMembership> AddedMemberships { get; } = [];
        public List<MembershipUpdate> Updates { get; } = [];
        public IError? AddFailure { get; set; }

        public Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
            long spaceId, long userId, CancellationToken cancellationToken = default)
        {
            UserLookups.Add((spaceId, userId));
            var found = Current is { } current && current.SpaceId == spaceId && current.UserId == userId
                ? TestSpaceInvites.Copy(current)
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result> AddAsync(SpaceMembership membership, CancellationToken cancellationToken = default)
        {
            AddedMemberships.Add(TestSpaceInvites.Copy(membership));
            return Task.FromResult(AddFailure is not null ? Result.Fail(AddFailure) : Result.Ok());
        }

        public Task<Result> UpdateAsync(
            SpaceMembership membership, long expectedVersion, CancellationToken cancellationToken = default)
        {
            Updates.Add(new MembershipUpdate(TestSpaceInvites.Copy(membership), expectedVersion));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<SpaceMembership?>> FindByPublicIdAsync(
            long spaceId, Guid membershipPublicId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invite acceptance selects the latest membership by user ID.");
    }

    private sealed class RoleReader(IEnumerable<SpaceRole> roles) : ISpaceRoleReader
    {
        private readonly SpaceRole[] items = roles.ToArray();

        public List<long> ReadSpaceIds { get; } = [];

        public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
            long spaceId, CancellationToken cancellationToken = default)
        {
            ReadSpaceIds.Add(spaceId);
            IReadOnlyList<SpaceRole> result = items.ToArray();
            return Task.FromResult(Result.Ok(result));
        }
    }
}
