using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Details;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Details;

[TestFixture]
public class GetSpaceMembershipUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid MembershipPublicId = Guid.Parse("01977b5b-164c-73b8-b681-1eabcc05a444");
    private static readonly Guid UserPublicId = Guid.Parse("01977b58-9ccc-7674-9554-3f85adeea223");

    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Suspended)]
    public async Task ExecuteAsync_ShouldReturnPersistedMembershipAndVersion(
        SpaceStatus status,
        SpaceMembershipStatus memberStatus)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(status, version: 3);
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: space.Id, systemKey: SpaceRoleSystemKey.Member);
        var membership = SpaceMembership.Reconstitute(55, MembershipPublicId, space.Id, 43, memberStatus, 9,
            Now.AddDays(-2), Now, memberStatus == SpaceMembershipStatus.Suspended ? Now : null, null,
            [MembershipRoleAssignment.Create(role.Id, space.OwnerUserId, Now.AddDays(-2))]);
        var snapshot = new SpaceMembershipReadSnapshot(membership, UserPublicId, null, "Member", [role]);
        var reader = new MembershipReader(Result.Ok<SpaceMembershipReadSnapshot?>(snapshot));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(space), MembershipPublicId));

        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value.Membership;
        Assert.Multiple(() =>
        {
            Assert.That(reader.Requests, Is.EqualTo(new[] { (space.Id, MembershipPublicId) }));
            Assert.That(result.Value.Version, Is.EqualTo(membership.Version));
            Assert.That(dto.MembershipPublicId, Is.EqualTo(MembershipPublicId));
            Assert.That(dto.User, Is.EqualTo(new SpaceMembershipUserDto(UserPublicId, null, "Member")));
            Assert.That(dto.Status, Is.EqualTo(memberStatus));
            Assert.That(dto.Roles, Is.EqualTo(new[]
            {
                new SpaceMembershipRoleDto(role.PublicId, SpaceRoleSystemKey.Member, role.Name),
            }));
            Assert.That(dto.JoinedAt, Is.EqualTo(membership.JoinedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(membership.UpdatedAt));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnMembershipNotFound()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var reader = new MembershipReader(Result.Ok<SpaceMembershipReadSnapshot?>(null));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(space), MembershipPublicId));

        AssertError(result, ErrorCodes.SpaceMembership.NotFound);
        Assert.That(reader.Requests, Is.EqualTo(new[] { (space.Id, MembershipPublicId) }));
    }

    [TestCase(7L, 91L)]
    [TestCase(8L, 43L)]
    public async Task ExecuteAsync_ShouldMaskIneligibleMembership(long membershipSpaceId, long memberUserId)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space(ownerUserId: 21);
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(ownerUserId: 91, version: 2);
        var repository = new FakeSpaceRepository();
        repository.Seed(persistedSpace);
        var membership = SpaceMembership.Reconstitute(55, MembershipPublicId, membershipSpaceId, memberUserId,
            SpaceMembershipStatus.Active, 1, Now, Now, null, null,
            [MembershipRoleAssignment.Create(101, persistedSpace.OwnerUserId, Now)]);
        var role = TestSpaceAuthorizationSnapshots.Role(spaceId: membershipSpaceId, systemKey: SpaceRoleSystemKey.Member);
        var snapshot = new SpaceMembershipReadSnapshot(membership, UserPublicId, "member", null, [role]);
        var reader = new MembershipReader(Result.Ok<SpaceMembershipReadSnapshot?>(snapshot));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(authorizedSpace), MembershipPublicId));

        AssertError(result, ErrorCodes.SpaceMembership.NotFound);
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedSpaceState(SpaceStatus status)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(TestSpaceAuthorizationSnapshots.Space(status, version: 2));
        var reader = new MembershipReader(Result.Ok<SpaceMembershipReadSnapshot?>(null));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(authorizedSpace), MembershipPublicId));

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(reader.Requests, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveMembershipReadFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var failure = CommonError.DependencyUnavailable();
        var reader = new MembershipReader(Result.Fail<SpaceMembershipReadSnapshot?>(failure));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(space), MembershipPublicId));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectInvalidMembershipPublicId()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        var reader = new MembershipReader(Result.Ok<SpaceMembershipReadSnapshot?>(null));
        var useCase = new GetSpaceMembershipUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new GetSpaceMembershipQuery(Authorize(space), Guid.Empty));

        AssertError(result, ErrorCodes.SpaceMembership.PublicIdInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata["PropertyName"],
                Is.EqualTo(nameof(GetSpaceMembershipQuery.MembershipPublicId)));
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

    private static void AssertError(Result<SpaceMembershipResultDto> result, string code)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(code));
    }

    private sealed class MembershipReader(Result<SpaceMembershipReadSnapshot?> result) : ISpaceMembershipReader
    {
        public List<(long SpaceId, Guid MembershipPublicId)> Requests { get; } = [];

        public Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
            long spaceId,
            Guid membershipPublicId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((spaceId, membershipPublicId));
            return Task.FromResult(result);
        }

        public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
            long spaceId,
            long ownerUserId,
            long requesterUserId,
            string? cursor,
            int limit,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Membership details must use a single read.");
    }
}
