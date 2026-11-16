using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Invites.Creations;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Invites.Creations;

[TestFixture]
public sealed class CreateSpaceInviteUseCaseTests
{
    private static readonly DateTimeOffset Now = TestSpaceInvites.Now;

    [TestCase(null, false)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    public async Task ExecuteAsync_ShouldStageActiveViewerInvite(int? maxUses, bool owner)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var command = ValidCommand(space, maxUses, owner);
        int? addsObservedAtSave = null;
        harness.UnitOfWork.SaveChangesFailureFactory = _ =>
        {
            addsObservedAtSave = harness.Invites.AddedInvites.Count;
            return null;
        };

        var result = await harness.UseCase.ExecuteAsync(command);

        Assert.That(result.IsSuccess, Is.True);
        var added = harness.Invites.AddedInvites.Single();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Tokens.RequestedKinds, Is.EqualTo(new[] { TokenKind.SpaceInvite }));
            Assert.That(added.Id, Is.Zero);
            Assert.That(added.PublicId.Version, Is.EqualTo(7));
            Assert.That(added.PublicId.Variant, Is.InRange(8, 11));
            Assert.That(added.SpaceId, Is.EqualTo(space.Id));
            Assert.That(added.CreatedByUserId, Is.EqualTo(command.Space.UserId));
            Assert.That(added.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(added.GrantedRole, Is.EqualTo(SpaceRoleSystemKey.Viewer));
            Assert.That(added.ExpiresAt, Is.EqualTo(command.ExpiresAt));
            Assert.That(added.MaxUses, Is.EqualTo(maxUses));
            Assert.That(added.UseCount, Is.Zero);
            Assert.That(added.Version, Is.EqualTo(1));
            Assert.That(added.CreatedAt, Is.EqualTo(Now));
            Assert.That(added.UpdatedAt, Is.EqualTo(Now));
            Assert.That(added.RevokedAt, Is.Null);
            Assert.That(added.TokenHash, Is.EqualTo(TestSpaceInvites.Hash()));
            Assert.That(result.Value.InvitePublicId, Is.EqualTo(added.PublicId));
            Assert.That(result.Value.InviteToken, Is.EqualTo(TestSpaceInvites.PlainToken));
            Assert.That(result.Value.InviteUrl,
                Does.EndWith("/public/v2/invites/" + TestSpaceInvites.PlainToken));
            Assert.That(result.Value.ExpiresAt, Is.EqualTo(added.ExpiresAt));
            Assert.That(result.Value.MaxUses, Is.EqualTo(added.MaxUses));
            Assert.That(result.Value.Version, Is.EqualTo(added.Version));
            Assert.That(addsObservedAtSave, Is.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectInvalidIssuedHash()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        harness.Tokens.Issued = new IssuedToken(TestSpaceInvites.PlainToken, TestSpaceInvites.PlainToken);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(space));

        Assert.That(result.IsFailed, Is.True);
        AssertNoWrites(harness);
        Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackAddFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        harness.Invites.AddFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(space));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(space));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackCommitFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        harness.Transaction.CommitFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(space));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.EqualTo(1));
        });
    }

    [TestCase(null, ErrorCodes.Auth.SpaceNotFound)]
    [TestCase(SpaceStatus.Archived, ErrorCodes.Auth.SpaceStateNotAllowed)]
    [TestCase(SpaceStatus.Deleting, ErrorCodes.Auth.SpaceStateNotAllowed)]
    [TestCase(SpaceStatus.Deleted, ErrorCodes.Auth.SpaceStateNotAllowed)]
    public async Task ExecuteAsync_ShouldRejectChangedSpace(SpaceStatus? latestStatus, string errorCode)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space();
        var latestSpace = latestStatus is { } status
            ? TestSpaceAuthorizationSnapshots.Space(status, version: 2)
            : null;
        var harness = new Harness(latestSpace);

        var result = await harness.UseCase.ExecuteAsync(ValidCommand(authorizedSpace));

        AssertError(result, errorCode);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Tokens.RequestedKinds, Is.Empty);
            AssertNoWrites(harness);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingManagePermission()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var context = Authorize(space, SpacePermission.MembersView, requirePermission: false);

        var result = await harness.UseCase.ExecuteAsync(
            new CreateSpaceInviteCommand(context, Now.AddDays(1), 1));

        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMcpContext()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);

        var result = await harness.UseCase.ExecuteAsync(
            new CreateSpaceInviteCommand(McpContext(space), Now.AddDays(1), 1));

        AssertError(result, ErrorCodes.Auth.TokenTypeNotAllowed);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public void ExecuteAsync_ShouldThrowArgumentNullException()
    {
        var harness = new Harness(TestSpaceAuthorizationSnapshots.Space());

        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireSpace()
    {
        var harness = new Harness(TestSpaceAuthorizationSnapshots.Space());

        var result = await harness.UseCase.ExecuteAsync(
            new CreateSpaceInviteCommand(null!, Now.AddDays(1), 1));

        AssertValidationError(result, ErrorCodes.Space.AuthorizationContextRequired,
            nameof(CreateSpaceInviteCommand.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    [TestCase(3600, 60)]
    public async Task ExecuteAsync_ShouldRejectInvalidExpiry(int secondsFromNow, int offsetMinutes)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var expiresAt = Now.AddSeconds(secondsFromNow).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

        var result = await harness.UseCase.ExecuteAsync(
            new CreateSpaceInviteCommand(Authorize(space), expiresAt, 1));

        AssertValidationError(result, ErrorCodes.SpaceInvite.ExpiresAtInvalid,
            nameof(CreateSpaceInviteCommand.ExpiresAt));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task ExecuteAsync_ShouldRejectInvalidMaxUses(int maxUses)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);

        var result = await harness.UseCase.ExecuteAsync(
            new CreateSpaceInviteCommand(Authorize(space), Now.AddDays(1), maxUses));

        AssertValidationError(result, ErrorCodes.SpaceInvite.MaxUsesInvalid,
            nameof(CreateSpaceInviteCommand.MaxUses));
        AssertNoDependencyCalls(harness);
    }

    private static CreateSpaceInviteCommand ValidCommand(
        Space space,
        int? maxUses = 1,
        bool owner = false) =>
        new(Authorize(space, owner: owner), Now.AddDays(1), maxUses);

    private static AuthorizedSpaceContext Authorize(
        Space space,
        SpacePermission permission = SpacePermission.InvitesManage,
        bool requirePermission = true,
        bool owner = false)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(userId: owner ? space.OwnerUserId : 42);
        var membership = owner
            ? null
            : TestSpaceAuthorizationSnapshots.Membership(spaceId: space.Id, userId: actor.UserId);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: space.Id, permissions: [permission]);
        SpacePermission[] required = requirePermission ? [SpacePermission.InvitesManage] : [];
        var query = new SpaceAuthorizationQuery(actor, space.PublicId, required, [SpaceStatus.Active]);
        SpaceRole[] roles = owner ? [] : [role];
        return SessionSpaceAuthorizationPolicy.Evaluate(
            query, new SpaceAuthorizationSnapshot(space, membership, roles)).Value;
    }

    private static AuthorizedSpaceContext McpContext(Space space) =>
        new(space.Id, space.PublicId, ActorType.Mcp, 42, null,
            [SpacePermission.InvitesManage], SpaceAuthorizationSource.Owner);

    private static void AssertError(Result<CreateSpaceInviteResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<CreateSpaceInviteResultDto> result, string errorCode, string propertyName)
    {
        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Tokens.RequestedKinds, Is.Empty);
            Assert.That(harness.Transaction.CommitCount, Is.Zero);
            Assert.That(harness.Transaction.RollbackCount, Is.Zero);
        });
    }

    private sealed class Harness
    {
        public Harness(Space? latestSpace)
        {
            if (latestSpace is not null)
            {
                Spaces.Seed(latestSpace);
            }

            Invites = new InviteRepository();
            Tokens = new TokenIssuer();
            UseCase = new CreateSpaceInviteUseCase(
                Spaces, Invites, Tokens, UnitOfWork, Transaction, new FakeClock(Now));
        }

        public FakeSpaceRepository Spaces { get; } = new();
        public InviteRepository Invites { get; }
        public TokenIssuer Tokens { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; } = new();
        public FakeTransactionExecutor Transaction { get; } = new();
        public CreateSpaceInviteUseCase UseCase { get; }
    }

    private sealed class TokenIssuer : ITokenIssuer
    {
        public List<TokenKind> RequestedKinds { get; } = [];
        public IssuedToken Issued { get; set; } =
            new(TestSpaceInvites.PlainToken, TestSpaceInvites.TokenHashValue);

        public IssuedToken Issue(TokenKind kind)
        {
            RequestedKinds.Add(kind);
            return Issued;
        }
    }

    private sealed class InviteRepository : ISpaceInviteRepository
    {
        public List<SpaceInvite> AddedInvites { get; } = [];
        public IError? AddFailure { get; set; }

        public Task<Result> AddAsync(SpaceInvite invite, CancellationToken cancellationToken = default)
        {
            AddedInvites.Add(TestSpaceInvites.Copy(invite));
            return Task.FromResult(AddFailure is not null ? Result.Fail(AddFailure) : Result.Ok());
        }

        public Task<Result<SpaceInvite?>> FindByPublicIdAsync(
            long spaceId, Guid invitePublicId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invite creation does not read by public ID.");

        public Task<Result<SpaceInvite?>> FindByTokenHashAsync(
            TokenHash tokenHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invite creation does not read by token hash.");

        public Task<Result> UpdateAsync(
            SpaceInvite invite, long expectedVersion, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invite creation does not update an existing invite.");
    }
}
