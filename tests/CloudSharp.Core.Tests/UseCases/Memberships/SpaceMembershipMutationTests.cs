using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships;

public abstract class SpaceMembershipMutationTests
{
    protected const long SpaceId = 7;
    protected static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);
    private const long CurrentOwnerUserId = 91;
    private const long MemberUserId = 43;
    private static readonly Guid MembershipPublicId = Guid.Parse("01977b5b-164c-73b8-b681-1eabcc05a444");
    private static readonly Guid UserPublicId = Guid.Parse("01977b58-9ccc-7674-9554-3f85adeea223");
    private static readonly SpaceMembershipUserDto User = new(UserPublicId, "member", "Member");

    protected abstract SpaceMembershipStatus InitialStatus { get; }

    protected abstract Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        Harness harness,
        AuthorizedSpaceContext space,
        Guid membershipPublicId,
        long expectedVersion);

    [TestCase("01977b5b-164c-43b8-b681-1eabcc05a444", 4L,
        ErrorCodes.SpaceMembership.PublicIdInvalid, "MembershipPublicId")]
    [TestCase("01977b5b-164c-73b8-b681-1eabcc05a444", 0L,
        ErrorCodes.Space.ExpectedVersionInvalid, "ExpectedVersion")]
    public async Task ExecuteAsync_ShouldRejectInvalidInput(
        string publicId,
        long expectedVersion,
        string errorCode,
        string propertyName)
    {
        var harness = new Harness(InitialStatus);

        var result = await ExecuteAsync(harness, harness.Context, Guid.Parse(publicId), expectedVersion);

        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireSession()
    {
        var harness = new Harness(InitialStatus);
        var session = harness.Context;
        var mcp = new AuthorizedSpaceContext(
            session.SpaceId, session.SpacePublicId, ActorType.Mcp, session.UserId,
            session.MembershipId, session.EffectivePermissions, session.AuthorizationSource);

        var result = await ExecuteAsync(harness, mcp, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Auth.TokenTypeNotAllowed);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireMembersManage()
    {
        var harness = new Harness(InitialStatus);
        var context = Authorize(SpacePermission.MembersView);

        var result = await ExecuteAsync(harness, context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        var harness = new Harness(InitialStatus, latestSpaceStatus: null);

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Auth.SpaceNotFound);
        Assert.That(harness.MembershipReader.Reads, Is.Empty);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRequireLatestActiveSpace(SpaceStatus latestStatus)
    {
        var harness = new Harness(InitialStatus, latestSpaceStatus: latestStatus);

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(result.Errors.Single(), Is.TypeOf<SpaceAuthorizationError>()
            .With.Property(nameof(SpaceAuthorizationError.ActualSpaceStatus)).EqualTo(latestStatus));
        Assert.That(harness.MembershipReader.Reads, Is.Empty);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [TestCase(null, MemberUserId)]
    [TestCase(8L, MemberUserId)]
    [TestCase(SpaceId, CurrentOwnerUserId)]
    public async Task ExecuteAsync_ShouldReturnMembershipNotFound(long? targetSpaceId, long targetUserId)
    {
        var harness = new Harness(InitialStatus, targetSpaceId: targetSpaceId, targetUserId: targetUserId);

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.SpaceMembership.NotFound);
        Assert.That(harness.MembershipReader.Reads, Is.EqualTo(new[] { (SpaceId, harness.Original.PublicId) }));
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectLeftMembership()
    {
        var harness = new Harness(SpaceMembershipStatus.Left);

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.SpaceMembership.Inactive);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersion()
    {
        var harness = new Harness(InitialStatus);

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version - 1);

        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateMembershipReadFailure()
    {
        var harness = new Harness(InitialStatus);
        var failure = CommonError.DependencyUnavailable();
        harness.MembershipReader.Failure = failure;

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollBackRegistrationFailure()
    {
        var harness = new Harness(InitialStatus);
        var failure = CommonError.DependencyUnavailable();
        harness.MembershipRepository.Failure = failure;

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        Assert.That(harness.MembershipRepository.Updates, Has.Count.EqualTo(1));
        Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollBackSaveConflict()
    {
        var harness = new Harness(InitialStatus);
        var failure = CommonError.PreconditionFailed();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        Assert.That(harness.MembershipRepository.Updates.Single().ExpectedVersion, Is.EqualTo(harness.Original.Version));
        Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
        AssertRolledBack(harness);
    }

    protected static void AssertError(Result<SpaceMembershipResultDto> result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedCode));
    }

    protected static void AssertNoWrites(Harness harness)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.MembershipRepository.Updates, Is.Empty);
            Assert.That(harness.Spaces.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        }
    }

    protected static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Spaces.SearchedPublicIds, Is.Empty);
            Assert.That(harness.MembershipReader.Reads, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        }
    }

    protected static void AssertRolledBack(Harness harness)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Transactions.CommitCount, Is.Zero);
            Assert.That(harness.Transactions.RollbackCount, Is.EqualTo(1));
        }
    }

    protected static SpaceMembership AssertSaved(
        Harness harness,
        Result<SpaceMembershipResultDto> result,
        SpaceMembershipStatus expectedStatus,
        IReadOnlyList<SpaceRole> expectedRoles)
    {
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.MembershipRepository.Updates, Has.Count.EqualTo(1));
        var (membership, expectedVersion) = harness.MembershipRepository.Updates.Single();
        var original = harness.Original;
        var dto = result.Value.Membership;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Spaces.SearchedIds, Is.EqualTo(new[] { SpaceId }));
            Assert.That(harness.MembershipReader.Reads, Is.EqualTo(new[] { (SpaceId, original.PublicId) }));
            Assert.That(membership.Id, Is.EqualTo(original.Id));
            Assert.That(membership.PublicId, Is.EqualTo(original.PublicId));
            Assert.That(membership.UserId, Is.EqualTo(original.UserId));
            Assert.That(membership.SpaceId, Is.EqualTo(original.SpaceId));
            Assert.That(membership.JoinedAt, Is.EqualTo(original.JoinedAt));
            Assert.That(membership.UpdatedAt, Is.EqualTo(Now));
            Assert.That(membership.Status, Is.EqualTo(expectedStatus));
            Assert.That(membership.LeftAt, Is.Null);
            Assert.That(membership.Version, Is.EqualTo(original.Version + 1));
            Assert.That(expectedVersion, Is.EqualTo(original.Version));
            Assert.That(dto.MembershipPublicId, Is.EqualTo(original.PublicId));
            Assert.That(dto.User, Is.EqualTo(User));
            Assert.That(dto.Status, Is.EqualTo(expectedStatus));
            Assert.That(dto.Roles, Is.EquivalentTo(expectedRoles.Select(role =>
                new SpaceMembershipRoleDto(role.PublicId, role.SystemKey!.Value, role.Name))));
            Assert.That(dto.JoinedAt, Is.EqualTo(original.JoinedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Version, Is.EqualTo(original.Version + 1));
            Assert.That(harness.UpdatesObservedAtSave, Is.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        }

        return membership;
    }

    private static AuthorizedSpaceContext Authorize(SpacePermission permission)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var space = TestSpaceAuthorizationSnapshots.Space(id: SpaceId, version: 8);
        var membership = TestSpaceAuthorizationSnapshots.Membership(userId: actor.UserId, spaceId: space.Id);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: space.Id, permissions: [permission]);
        var query = TestSpaceAuthorizationSnapshots.Query(actor, [permission], [SpaceStatus.Active], space.PublicId);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, new SpaceAuthorizationSnapshot(space, membership, [role])).Value;
    }

    private static SpaceMembership CopyMembership(SpaceMembership membership) =>
        SpaceMembership.Reconstitute(
            membership.Id, membership.PublicId, membership.SpaceId, membership.UserId,
            membership.Status, membership.Version, membership.JoinedAt, membership.UpdatedAt,
            membership.SuspendedAt, membership.LeftAt, membership.RoleAssignments);

    protected sealed class Harness
    {
        public Harness(
            SpaceMembershipStatus status,
            bool hasRoles = true,
            SpaceStatus? latestSpaceStatus = SpaceStatus.Active,
            long? targetSpaceId = SpaceId,
            long targetUserId = MemberUserId)
        {
            if (latestSpaceStatus is { } currentStatus)
            {
                Spaces.Seed(TestSpaceAuthorizationSnapshots.Space(
                    currentStatus, ownerUserId: CurrentOwnerUserId, id: SpaceId, version: 9));
            }

            OriginalRoles = hasRoles ? [KeptRole, RemovedRole] : [];
            var joinedAt = Now.AddDays(-7);
            var updatedAt = Now.AddDays(-1);
            Original = SpaceMembership.Reconstitute(
                55, MembershipPublicId, targetSpaceId ?? SpaceId, targetUserId, status, 4, joinedAt, updatedAt,
                status == SpaceMembershipStatus.Suspended ? updatedAt : null,
                status == SpaceMembershipStatus.Left ? updatedAt : null,
                OriginalRoles.Select(role => MembershipRoleAssignment.Create(role.Id, CurrentOwnerUserId, joinedAt)));
            MembershipReader = new SnapshotReader(targetSpaceId is null
                ? null
                : new SpaceMembershipReadSnapshot(Original, User.UserPublicId, User.UserName, User.DisplayName, OriginalRoles));
            Roles = new RoleReader { Items = [KeptRole, RemovedRole, AddedRole] };
            ReplacementRolePublicIds = [KeptRole.PublicId, AddedRole.PublicId];
            UnitOfWork.SaveChangesFailureFactory = _ =>
            {
                UpdatesObservedAtSave = MembershipRepository.Updates.Count;
                return null;
            };
        }

        public AuthorizedSpaceContext Context { get; } = Authorize(SpacePermission.MembersManage);
        public SpaceMembership Original { get; }
        public SpaceRole KeptRole { get; } = ManagedRole(101, "01977b5a-a709-7470-a957-1c707dbbc301", SpaceRoleSystemKey.Member);
        public SpaceRole RemovedRole { get; } = ManagedRole(102, "01977b5a-a709-7470-a957-1c707dbbc302", SpaceRoleSystemKey.Viewer);
        public SpaceRole AddedRole { get; } = ManagedRole(103, "01977b5a-a709-7470-a957-1c707dbbc303", SpaceRoleSystemKey.Admin);
        public IReadOnlyList<SpaceRole> OriginalRoles { get; }
        public IReadOnlyList<Guid> ReplacementRolePublicIds { get; }
        public FakeSpaceRepository Spaces { get; } = new();
        public SnapshotReader MembershipReader { get; }
        public UpdateRecorder MembershipRepository { get; } = new();
        public RoleReader Roles { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; } = new();
        public FakeTransactionExecutor Transactions { get; } = new();
        public FakeClock Clock { get; } = new(Now);
        public int? UpdatesObservedAtSave { get; private set; }

        private static SpaceRole ManagedRole(long id, string publicId, SpaceRoleSystemKey code)
        {
            var name = code.ToString();
            return SpaceRole.Reconstitute(
                id, Guid.Parse(publicId), SpaceId, name, name.ToUpperInvariant(), code,
                null, 0, true, 1, Now.AddDays(-10), Now.AddDays(-9), null, []);
        }

        public sealed class SnapshotReader(SpaceMembershipReadSnapshot? snapshot) : ISpaceMembershipReader
        {
            public List<(long SpaceId, Guid MembershipPublicId)> Reads { get; } = [];
            public IError? Failure { get; set; }

            public Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
                long spaceId, Guid membershipPublicId, CancellationToken cancellationToken = default)
            {
                Reads.Add((spaceId, membershipPublicId));
                return Task.FromResult(Failure is { } failure
                    ? Result.Fail<SpaceMembershipReadSnapshot?>(failure)
                    : Result.Ok(snapshot is null ? null : snapshot with { Membership = CopyMembership(snapshot.Membership) }));
            }

            public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
                long spaceId, long ownerUserId, string? cursor, int limit, string? sort, bool descending,
                CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Membership mutations read a single target.");
        }

        public sealed class UpdateRecorder : ISpaceMembershipRepository
        {
            public List<(SpaceMembership Membership, long ExpectedVersion)> Updates { get; } = [];
            public IError? Failure { get; set; }

            public Task<Result> UpdateAsync(
                SpaceMembership membership, long expectedVersion, CancellationToken cancellationToken = default)
            {
                Updates.Add((CopyMembership(membership), expectedVersion));
                return Task.FromResult(Failure is { } failure ? Result.Fail(failure) : Result.Ok());
            }

            public Task<Result<SpaceMembership?>> FindByPublicIdAsync(
                long spaceId, Guid membershipPublicId, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Membership mutation responses use a membership read snapshot.");

            public Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
                long spaceId, long userId, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Membership mutations select the target by its public ID.");
        }

        public sealed class RoleReader : ISpaceRoleReader
        {
            public IReadOnlyList<SpaceRole> Items { get; set; } = [];
            public List<long> ReadSpaceIds { get; } = [];
            public IError? Failure { get; set; }

            public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
                long spaceId, CancellationToken cancellationToken = default)
            {
                ReadSpaceIds.Add(spaceId);
                return Task.FromResult(Failure is { } failure
                    ? Result.Fail<IReadOnlyList<SpaceRole>>(failure)
                    : Result.Ok(Items));
            }
        }
    }
}
