using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.Listings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Invites.Listings;

[TestFixture]
public sealed class ListSpaceInvitesUseCaseTests
{
    [TestCase(SpaceStatus.Active, false)]
    [TestCase(SpaceStatus.Archived, false)]
    [TestCase(SpaceStatus.Active, true)]
    [TestCase(SpaceStatus.Archived, true)]
    public async Task ExecuteAsync_ShouldReturnScopedInvitePage(SpaceStatus status, bool owner)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space(status);
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(status, ownerUserId: 91, version: 2);
        var invite = TestSpaceInvites.Stored(spaceId: persistedSpace.Id);
        var otherSpaceInvite = TestSpaceInvites.Stored(id: 101, publicId: TestSpaceInvites.PublicId(101), spaceId: 8);
        var harness = new Harness(persistedSpace, invite, otherSpaceInvite);
        harness.Invites.NextCursor = "next-page";
        var query = new ListSpaceInvitesQuery(Authorize(authorizedSpace, owner: owner), "current-page", 1, "CreatedAt", false);

        var result = await harness.UseCase.ExecuteAsync(query);

        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.Requests, Is.EqualTo(new[]
            {
                new PageRequest(persistedSpace.Id, query.Cursor, query.Limit, query.Sort, query.Descending),
            }));
            Assert.That(result.Value.NextCursor, Is.EqualTo("next-page"));
            Assert.That(item.InvitePublicId, Is.EqualTo(invite.PublicId));
            Assert.That(item.Status, Is.EqualTo(invite.Status));
            Assert.That(item.ExpiresAt, Is.EqualTo(invite.ExpiresAt));
            Assert.That(item.MaxUses, Is.EqualTo(invite.MaxUses));
            Assert.That(item.UseCount, Is.EqualTo(invite.UseCount));
            Assert.That(item.CreatedAt, Is.EqualTo(invite.CreatedAt));
            Assert.That(item.UpdatedAt, Is.EqualTo(invite.UpdatedAt));
            Assert.That(item.RevokedAt, Is.EqualTo(invite.RevokedAt));
            Assert.That(typeof(SpaceInviteDto).GetProperties().Select(property => property.Name),
                Is.EquivalentTo(new[]
                {
                    nameof(SpaceInviteDto.InvitePublicId), nameof(SpaceInviteDto.Status),
                    nameof(SpaceInviteDto.ExpiresAt), nameof(SpaceInviteDto.MaxUses),
                    nameof(SpaceInviteDto.UseCount), nameof(SpaceInviteDto.CreatedAt),
                    nameof(SpaceInviteDto.UpdatedAt), nameof(SpaceInviteDto.RevokedAt),
                }));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_ShouldReturnFinalPage(bool empty)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        SpaceInvite[] invites = empty ? [] : [TestSpaceInvites.Stored()];
        var harness = new Harness(space, invites);
        harness.Invites.NextCursor = null;

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(Authorize(space)));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, empty ? Is.Empty : Has.Count.EqualTo(1));
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldUseDefaultPageArguments()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);

        await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(Authorize(space)));

        Assert.That(harness.Invites.Requests, Is.EqualTo(new[]
        {
            new PageRequest(space.Id, null, 50, null, true),
        }));
    }

    [TestCase(1)]
    [TestCase(100)]
    public async Task ExecuteAsync_ShouldPassRequestedPageLimit(int limit)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var query = new ListSpaceInvitesQuery(Authorize(space), "next-cursor", limit, "PublicId", false);

        await harness.UseCase.ExecuteAsync(query);

        Assert.That(harness.Invites.Requests.Single(),
            Is.EqualTo(new PageRequest(space.Id, query.Cursor, limit, query.Sort, false)));
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreservePageReadFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var invite = TestSpaceInvites.Stored();
        var harness = new Harness(space, invite);
        harness.Invites.Failure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(Authorize(space)));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(invite.Version, Is.EqualTo(3));
        });
    }

    [TestCase(null, ErrorCodes.Auth.SpaceNotFound)]
    [TestCase(SpaceStatus.Deleting, ErrorCodes.Auth.SpaceStateNotAllowed)]
    [TestCase(SpaceStatus.Deleted, ErrorCodes.Auth.SpaceStateNotAllowed)]
    public async Task ExecuteAsync_ShouldRejectChangedSpace(SpaceStatus? latestStatus, string errorCode)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space();
        var latestSpace = latestStatus is { } status
            ? TestSpaceAuthorizationSnapshots.Space(status, version: 2)
            : null;
        var harness = new Harness(latestSpace);

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(Authorize(authorizedSpace)));

        AssertError(result, errorCode);
        Assert.That(harness.Invites.Requests, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingManagePermission()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var context = Authorize(space, SpacePermission.MembersView, requirePermission: false);

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(context));

        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMcpContext()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(McpContext(space)));

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

        var result = await harness.UseCase.ExecuteAsync(new ListSpaceInvitesQuery(null!));

        AssertValidationError(result, ErrorCodes.Space.AuthorizationContextRequired,
            nameof(ListSpaceInvitesQuery.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceInvitesQuery.Limit))]
    [TestCase(101, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceInvitesQuery.Limit))]
    [TestCase(50, "", null, ErrorCodes.Space.ListCursorInvalid, nameof(ListSpaceInvitesQuery.Cursor))]
    [TestCase(50, " ", null, ErrorCodes.Space.ListCursorInvalid, nameof(ListSpaceInvitesQuery.Cursor))]
    [TestCase(50, null, "", ErrorCodes.Space.ListSortInvalid, nameof(ListSpaceInvitesQuery.Sort))]
    [TestCase(50, null, " ", ErrorCodes.Space.ListSortInvalid, nameof(ListSpaceInvitesQuery.Sort))]
    public async Task ExecuteAsync_ShouldRejectInvalidPagination(
        int limit,
        string? cursor,
        string? sort,
        string errorCode,
        string propertyName)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var harness = new Harness(space);
        var query = new ListSpaceInvitesQuery(Authorize(space), cursor, limit, sort);

        var result = await harness.UseCase.ExecuteAsync(query);

        AssertValidationError(result, errorCode, propertyName);
        AssertNoDependencyCalls(harness);
    }

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
        SpaceRole[] roles = owner ? [] : [role];
        var query = new SpaceAuthorizationQuery(actor, space.PublicId, required,
            [SpaceStatus.Active, SpaceStatus.Archived]);
        return SessionSpaceAuthorizationPolicy.Evaluate(
            query, new SpaceAuthorizationSnapshot(space, membership, roles)).Value;
    }

    private static AuthorizedSpaceContext McpContext(Space space) =>
        new(space.Id, space.PublicId, ActorType.Mcp, 42, null,
            [SpacePermission.InvitesManage], SpaceAuthorizationSource.Owner);

    private static void AssertError(Result<SpaceInvitePageDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<SpaceInvitePageDto> result, string errorCode, string propertyName)
    {
        AssertError(result, errorCode);
        Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Invites.Requests, Is.Empty);
        });
    }

    private sealed class Harness
    {
        public Harness(Space? latestSpace = null, params SpaceInvite[] invites)
        {
            if (latestSpace is not null)
            {
                Spaces.Seed(latestSpace);
            }

            Invites = new InviteReader(invites);
            UseCase = new ListSpaceInvitesUseCase(Spaces, Invites);
        }

        public FakeSpaceRepository Spaces { get; } = new();
        public InviteReader Invites { get; }
        public ListSpaceInvitesUseCase UseCase { get; }
    }

    private sealed record PageRequest(
        long SpaceId,
        string? Cursor,
        int Limit,
        string? Sort,
        bool Descending);

    private sealed class InviteReader(IReadOnlyList<SpaceInvite> items) : ISpaceInviteReader
    {
        public List<PageRequest> Requests { get; } = [];
        public string? NextCursor { get; set; }
        public IError? Failure { get; set; }

        public Task<Result<SpaceInviteReadPage>> ReadPageAsync(
            long spaceId,
            string? cursor,
            int limit,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new PageRequest(spaceId, cursor, limit, sort, descending));
            if (Failure is not null)
            {
                return Task.FromResult(Result.Fail<SpaceInviteReadPage>(Failure));
            }

            var page = items
                .Where(invite => invite.SpaceId == spaceId)
                .Take(limit)
                .Select(TestSpaceInvites.Copy)
                .ToArray();
            return Task.FromResult(Result.Ok(new SpaceInviteReadPage(page, NextCursor)));
        }
    }
}
