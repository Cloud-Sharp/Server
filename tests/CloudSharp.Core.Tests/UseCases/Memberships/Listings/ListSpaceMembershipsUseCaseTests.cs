using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Listings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Listings;

[TestFixture]
public class ListSpaceMembershipsUseCaseTests
{
    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Suspended)]
    public async Task ExecuteAsync_ShouldReturnMembershipPage(SpaceStatus status, SpaceMembershipStatus memberStatus)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space(status, ownerUserId: 21);
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(status, ownerUserId: 91, version: 2);
        var repository = new FakeSpaceRepository();
        repository.Seed(persistedSpace);
        var membership = TestSpaceAuthorizationSnapshots.Membership(memberStatus, userId: 43, spaceId: persistedSpace.Id);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: persistedSpace.Id, systemKey: SpaceRoleSystemKey.Member);
        var snapshot = new SpaceMembershipReadSnapshot(membership,
            Guid.Parse("01977b58-9ccc-7674-9554-3f85adeea223"), "member", "Member", [role]);
        var reader = new MembershipReader(Result.Ok(new SpaceMembershipReadPage([snapshot], "next-page")));
        var query = new ListSpaceMembershipsQuery(Authorize(authorizedSpace), "current-page", 1, Descending: false);
        var useCase = new ListSpaceMembershipsUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(query);

        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(reader.Requests, Is.EqualTo(new[]
            {
                new PageRequest(persistedSpace.Id, persistedSpace.OwnerUserId, query.Cursor, query.Limit,
                    query.Sort, query.Descending),
            }));
            Assert.That(result.Value.NextCursor, Is.EqualTo("next-page"));
            Assert.That(item.MembershipPublicId, Is.EqualTo(membership.PublicId));
            Assert.That(item.User, Is.EqualTo(new SpaceMembershipUserDto(snapshot.UserPublicId, "member", "Member")));
            Assert.That(item.Status, Is.EqualTo(memberStatus));
            Assert.That(item.Roles, Is.EqualTo(new[]
            {
                new SpaceMembershipRoleDto(role.PublicId, SpaceRoleSystemKey.Member, role.Name),
            }));
            Assert.That(item.JoinedAt, Is.EqualTo(membership.JoinedAt));
            Assert.That(item.UpdatedAt, Is.EqualTo(membership.UpdatedAt));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmptyPage()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var reader = new MembershipReader(Result.Ok(new SpaceMembershipReadPage([], null)));
        var useCase = new ListSpaceMembershipsUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceMembershipsQuery(Authorize(space)));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedSpaceState(SpaceStatus status)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(TestSpaceAuthorizationSnapshots.Space(status, version: 2));
        var reader = new MembershipReader(Result.Ok(new SpaceMembershipReadPage([], null)));
        var useCase = new ListSpaceMembershipsUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceMembershipsQuery(Authorize(authorizedSpace)));

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(reader.Requests, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreservePageReadFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var reader = new MembershipReader(Result.Fail<SpaceMembershipReadPage>(CommonError.DependencyUnavailable()));
        var useCase = new ListSpaceMembershipsUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceMembershipsQuery(Authorize(space)));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
    }

    [TestCase(0, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceMembershipsQuery.Limit))]
    [TestCase(101, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceMembershipsQuery.Limit))]
    [TestCase(50, " ", null, ErrorCodes.Space.ListCursorInvalid, nameof(ListSpaceMembershipsQuery.Cursor))]
    [TestCase(50, null, " ", ErrorCodes.Space.ListSortInvalid, nameof(ListSpaceMembershipsQuery.Sort))]
    public async Task ExecuteAsync_ShouldRejectInvalidPagination(
        int limit,
        string? cursor,
        string? sort,
        string errorCode,
        string propertyName)
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        var reader = new MembershipReader(Result.Ok(new SpaceMembershipReadPage([], null)));
        var useCase = new ListSpaceMembershipsUseCase(repository, reader);
        var query = new ListSpaceMembershipsQuery(Authorize(space), cursor, limit, sort);

        var result = await useCase.ExecuteAsync(query);

        AssertError(result, errorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
            Assert.That(repository.SearchedIds, Is.Empty);
            Assert.That(reader.Requests, Is.Empty);
        });
    }

    private static AuthorizedSpaceContext Authorize(Space space)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var membership = TestSpaceAuthorizationSnapshots.Membership(spaceId: space.Id, userId: actor.UserId);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: space.Id, permissions: [SpacePermission.MembersView]);
        var query = TestSpaceAuthorizationSnapshots.Query(actor,
            permissions: [SpacePermission.MembersView], spacePublicId: space.PublicId);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, new SpaceAuthorizationSnapshot(space, membership, [role])).Value;
    }

    private static void AssertError(Result<SpaceMembershipPageDto> result, string code)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(code));
    }

    private sealed record PageRequest(long SpaceId, long OwnerUserId, string? Cursor, int Limit, string? Sort, bool Descending);

    private sealed class MembershipReader(Result<SpaceMembershipReadPage> result) : ISpaceMembershipReader
    {
        public List<PageRequest> Requests { get; } = [];

        public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
            long spaceId,
            long ownerUserId,
            string? cursor,
            int limit,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new PageRequest(spaceId, ownerUserId, cursor, limit, sort, descending));
            return Task.FromResult(result);
        }

        public Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
            long spaceId,
            Guid membershipPublicId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Membership list must use a page read.");
    }
}
