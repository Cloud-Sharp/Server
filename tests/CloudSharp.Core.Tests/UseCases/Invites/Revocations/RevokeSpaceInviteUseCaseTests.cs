using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.Revocations;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Invites.Revocations;

[TestFixture]
public sealed class RevokeSpaceInviteUseCaseTests
{
    private const long SpaceId = 7;
    private static readonly DateTimeOffset Now = TestSpaceInvites.Now;
    private static readonly Guid InvitePublicId = TestSpaceInvites.PublicId(100);

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_ShouldRevokeInviteWithoutChangingMembers(bool owner)
    {
        var target = StoredInvite();
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(target, space);
        var existingMembership = TestSpaceAuthorizationSnapshots.Membership(
            userId: 88, spaceId: SpaceId, roleIds: 101);
        var originalMembership = TestSpaceInvites.Copy(existingMembership);
        int? updatesObservedAtSave = null;
        harness.UnitOfWork.SaveChangesFailureFactory = _ =>
        {
            updatesObservedAtSave = harness.Invites.Updates.Count;
            return null;
        };

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(space, owner));

        Assert.That(result.IsSuccess, Is.True);
        var update = harness.Invites.Updates.Single();
        var updated = update.Invite;
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Reads, Is.EqualTo(new[] { (SpaceId, InvitePublicId) }));
            Assert.That(update.ExpectedVersion, Is.EqualTo(target.Version));
            Assert.That(updated.Id, Is.EqualTo(target.Id));
            Assert.That(updated.PublicId, Is.EqualTo(target.PublicId));
            Assert.That(updated.SpaceId, Is.EqualTo(target.SpaceId));
            Assert.That(updated.CreatedByUserId, Is.EqualTo(target.CreatedByUserId));
            Assert.That(updated.TokenHash, Is.EqualTo(target.TokenHash));
            Assert.That(updated.Status, Is.EqualTo(SpaceInviteStatus.Revoked));
            Assert.That(updated.ExpiresAt, Is.EqualTo(target.ExpiresAt));
            Assert.That(updated.MaxUses, Is.EqualTo(target.MaxUses));
            Assert.That(updated.UseCount, Is.EqualTo(target.UseCount));
            Assert.That(updated.CreatedAt, Is.EqualTo(target.CreatedAt));
            Assert.That(updated.UpdatedAt, Is.EqualTo(Now));
            Assert.That(updated.RevokedAt, Is.EqualTo(Now));
            Assert.That(updated.Version, Is.EqualTo(target.Version + 1));
            Assert.That(result.Value.Version, Is.EqualTo(updated.Version));
            Assert.That(existingMembership.Status, Is.EqualTo(originalMembership.Status));
            Assert.That(existingMembership.Version, Is.EqualTo(originalMembership.Version));
            Assert.That(existingMembership.UpdatedAt, Is.EqualTo(originalMembership.UpdatedAt));
            Assert.That(existingMembership.RoleAssignments, Is.EquivalentTo(originalMembership.RoleAssignments));
            Assert.That(updatesObservedAtSave, Is.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    [TestCase(0L)]
    [TestCase(8L)]
    public async Task ExecuteAsync_ShouldReturnNotFound(long targetSpaceId)
    {
        var target = targetSpaceId == 0 ? null : StoredInvite(spaceId: targetSpaceId);
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.SpaceInvite.NotFound);
        AssertNoWrites(harness);
        Assert.That(target is null || target.Version == 3, Is.True);
    }

    [TestCase(SpaceInviteStatus.Revoked)]
    [TestCase(SpaceInviteStatus.Expired)]
    public async Task ExecuteAsync_ShouldRejectTerminalInvite(SpaceInviteStatus status)
    {
        var target = StoredInvite(status: status);
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.SpaceInvite.InvalidState);
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(status));
            Assert.That(target.Version, Is.EqualTo(3));
            Assert.That(target.RevokedAt, Is.EqualTo(status == SpaceInviteStatus.Revoked
                ? Now.AddDays(-1)
                : null));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleRequestVersion()
    {
        var target = StoredInvite();
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));

        var result = await harness.UseCase.ExecuteAsync(ValidCommand() with { ExpectedVersion = target.Version - 1 });

        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertNoWrites(harness);
        Assert.That(target.Status, Is.EqualTo(SpaceInviteStatus.Active));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveConcurrencyConflict()
    {
        var target = StoredInvite();
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Updates.Single().ExpectedVersion, Is.EqualTo(target.Version));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackUpdateFailure()
    {
        var target = StoredInvite();
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));
        harness.Invites.UpdateFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackCommitFailure()
    {
        var target = StoredInvite();
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));
        harness.Transaction.CommitFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveInviteReadFailure()
    {
        var target = StoredInvite();
        var harness = new Harness(target, StoredSpace(SpaceStatus.Active));
        harness.Invites.ReadFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        AssertNoWrites(harness);
    }

    [TestCase(null, ErrorCodes.Auth.SpaceNotFound)]
    [TestCase(SpaceStatus.Archived, ErrorCodes.Auth.SpaceStateNotAllowed)]
    [TestCase(SpaceStatus.Deleting, ErrorCodes.Auth.SpaceStateNotAllowed)]
    [TestCase(SpaceStatus.Deleted, ErrorCodes.Auth.SpaceStateNotAllowed)]
    public async Task ExecuteAsync_ShouldRejectChangedSpace(SpaceStatus? latestStatus, string errorCode)
    {
        var latestSpace = latestStatus is { } status ? StoredSpace(status) : null;
        var harness = new Harness(StoredInvite(), latestSpace);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        AssertError(result, errorCode);
        AssertNoWrites(harness);
        Assert.That(harness.Invites.Reads, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_ShouldThrowArgumentNullException()
    {
        var harness = new Harness(StoredInvite(), StoredSpace(SpaceStatus.Active));

        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireSpace()
    {
        var harness = new Harness(StoredInvite(), StoredSpace(SpaceStatus.Active));

        var result = await harness.UseCase.ExecuteAsync(
            new RevokeSpaceInviteCommand(null!, InvitePublicId, 3));

        AssertValidationError(result, ErrorCodes.Space.AuthorizationContextRequired,
            nameof(RevokeSpaceInviteCommand.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("6f9619ff-8b86-4d11-b42d-00c04fc964ff")]
    [TestCase("01920000-0000-7000-0000-000000000100")]
    public async Task ExecuteAsync_ShouldRejectInvalidPublicId(string publicId)
    {
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(StoredInvite(), space);

        var result = await harness.UseCase.ExecuteAsync(
            new RevokeSpaceInviteCommand(Authorize(space), Guid.Parse(publicId), 3));

        AssertValidationError(result, ErrorCodes.SpaceInvite.PublicIdInvalid,
            nameof(RevokeSpaceInviteCommand.InvitePublicId));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldRejectInvalidExpectedVersion(long expectedVersion)
    {
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(StoredInvite(), space);

        var result = await harness.UseCase.ExecuteAsync(
            new RevokeSpaceInviteCommand(Authorize(space), InvitePublicId, expectedVersion));

        AssertValidationError(result, ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(RevokeSpaceInviteCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    private static RevokeSpaceInviteCommand ValidCommand(
        Space? space = null,
        bool owner = false)
    {
        space ??= StoredSpace(SpaceStatus.Active);
        return new RevokeSpaceInviteCommand(Authorize(space, owner: owner), InvitePublicId, 3);
    }

    private static SpaceInvite StoredInvite(
        long spaceId = SpaceId,
        SpaceInviteStatus status = SpaceInviteStatus.Active) =>
        TestSpaceInvites.Stored(spaceId: spaceId, status: status);

    private static Space StoredSpace(SpaceStatus status) =>
        TestSpaceAuthorizationSnapshots.Space(status, id: SpaceId, version: 9);

    private static AuthorizedSpaceContext Authorize(Space space, bool owner = false)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(userId: owner ? space.OwnerUserId : 42);
        var membership = owner
            ? null
            : TestSpaceAuthorizationSnapshots.Membership(spaceId: space.Id, userId: actor.UserId);
        var role = TestSpaceAuthorizationSnapshots.Role(
            spaceId: space.Id, permissions: [SpacePermission.InvitesManage]);
        SpaceRole[] roles = owner ? [] : [role];
        var query = new SpaceAuthorizationQuery(
            actor, space.PublicId, [SpacePermission.InvitesManage], [SpaceStatus.Active]);
        return SessionSpaceAuthorizationPolicy.Evaluate(
            query, new SpaceAuthorizationSnapshot(space, membership, roles)).Value;
    }

    private static void AssertError(Result<RevokeSpaceInviteResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<RevokeSpaceInviteResultDto> result, string errorCode, string propertyName)
    {
        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Updates, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Invites.Reads, Is.Empty);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    private sealed class Harness
    {
        public Harness(SpaceInvite? target, Space? latestSpace)
        {
            if (latestSpace is not null)
            {
                Spaces.Seed(latestSpace);
            }

            Invites = new InviteRepository(target);
            UseCase = new RevokeSpaceInviteUseCase(
                Spaces, Invites, UnitOfWork, Transaction, new FakeClock(Now));
        }

        public FakeSpaceRepository Spaces { get; } = new();
        public InviteRepository Invites { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; } = new();
        public FakeTransactionExecutor Transaction { get; } = new();
        public RevokeSpaceInviteUseCase UseCase { get; }
    }

    private sealed record InviteUpdate(SpaceInvite Invite, long ExpectedVersion);

    private sealed class InviteRepository(SpaceInvite? target) : ISpaceInviteRepository
    {
        public List<(long SpaceId, Guid InvitePublicId)> Reads { get; } = [];
        public List<InviteUpdate> Updates { get; } = [];
        public IError? ReadFailure { get; set; }
        public IError? UpdateFailure { get; set; }

        public Task<Result<SpaceInvite?>> FindByPublicIdAsync(
            long spaceId, Guid invitePublicId, CancellationToken cancellationToken = default)
        {
            Reads.Add((spaceId, invitePublicId));
            if (ReadFailure is not null)
            {
                return Task.FromResult(Result.Fail<SpaceInvite?>(ReadFailure));
            }

            var found = target is not null && target.SpaceId == spaceId && target.PublicId == invitePublicId
                ? TestSpaceInvites.Copy(target)
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result> UpdateAsync(
            SpaceInvite invite, long expectedVersion, CancellationToken cancellationToken = default)
        {
            Updates.Add(new InviteUpdate(TestSpaceInvites.Copy(invite), expectedVersion));
            return Task.FromResult(UpdateFailure is not null ? Result.Fail(UpdateFailure) : Result.Ok());
        }

        public Task<Result<SpaceInvite?>> FindByTokenHashAsync(
            TokenHash tokenHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Revocation selects an invite by public ID.");

        public Task<Result> AddAsync(SpaceInvite invite, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Revocation does not add an invite.");
    }
}
