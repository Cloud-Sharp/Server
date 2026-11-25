using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MembershipLeaveEndpointTests : MembershipIntegrationTestBase
{
    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Active)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Suspended)]
    public async Task Leave_ShouldTransitionCurrentMembershipAndRepeatIdempotently(
        SpaceStatus spaceStatus,
        SpaceMembershipStatus membershipStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: spaceStatus);
        var history = await SeedMembershipAsync(space, actor, owner, SpaceMembershipStatus.Left, SpaceRoleSystemKey.Member);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var current = await SeedMembershipAsync(space, actor, owner, membershipStatus, SpaceRoleSystemKey.Viewer);
        var originalLinks = await GetSpaceMembershipRoleLinksAsync(space.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));

        using var response = await Api.SendAsync(HttpMethod.Delete, LeavePath(space.PublicId), actor.AccessToken);
        await AssertNoContentAsync(response);

        var rows = await GetSpaceMembershipsAsync(space.Id);
        var stored = rows.Single(row => row.Id == current.Id);
        var storedHistory = rows.Single(row => row.Id == history.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(stored.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(stored.Version, Is.EqualTo(current.Version + 1));
            Assert.That(stored.JoinedAt, Is.EqualTo(current.JoinedAt));
            Assert.That(stored.UpdatedAt, Is.EqualTo(Clock.UtcNow).Within(TimeSpan.FromMicroseconds(1)));
            Assert.That(stored.LeftAt, Is.EqualTo(stored.UpdatedAt));
            Assert.That(stored.SuspendedAt, Is.Null);
            Assert.That(storedHistory.Version, Is.EqualTo(history.Version));
            Assert.That(storedHistory.LeftAt, Is.EqualTo(history.LeftAt));
        });

        Clock.Advance(TimeSpan.FromMinutes(1));
        using var repeated = await Api.SendAsync(HttpMethod.Delete, LeavePath(space.PublicId), actor.AccessToken);
        await AssertNoContentAsync(repeated);

        var repeatedRows = await GetSpaceMembershipsAsync(space.Id);
        var repeatedLinks = await GetSpaceMembershipRoleLinksAsync(space.Id);
        Assert.Multiple(() =>
        {
            Assert.That(repeatedRows.Select(row => (row.Id, row.Status, row.Version, row.UpdatedAt, row.LeftAt)),
                Is.EquivalentTo(rows.Select(row => (row.Id, row.Status, row.Version, row.UpdatedAt, row.LeftAt))));
            Assert.That(repeatedLinks.Select(link => (link.MembershipId, link.RoleId, link.AssignedByUserId, link.AssignedAt)),
                Is.EquivalentTo(originalLinks.Select(link => (link.MembershipId, link.RoleId, link.AssignedByUserId, link.AssignedAt))));
        });

        using var spaceResponse = await Api.GetAsync(SpacePath(space.PublicId), actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(spaceResponse, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task Leave_AsCurrentOwner_ShouldReturn403(SpaceStatus status)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: status);

        using var response = await Api.SendAsync(HttpMethod.Delete, LeavePath(space.PublicId), owner.AccessToken);

        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
    }

    [Test]
    public async Task Leave_WithoutAnEligibleOwnRelationship_ShouldReturnSame404()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var visibleMember = await RegisterSpaceUserAsync();
        var administrator = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var active = await SeedSpaceAsync(owner);
        var other = await SeedSpaceAsync(owner);
        var deleting = await SeedSpaceAsync(owner, status: SpaceStatus.Deleting);
        var deleted = await SeedSpaceAsync(owner, status: SpaceStatus.Deleted);
        await SeedMembershipAsync(active, visibleMember, owner);
        await SeedMembershipAsync(other, actor, owner);
        await SeedMembershipAsync(deleting, actor, owner);
        await SeedMembershipAsync(deleted, actor, owner);

        var errors = new List<ErrorEnvelope>();
        foreach (var (spaceId, caller) in new[]
                 {
                     (Guid.CreateVersion7(), actor),
                     (active.PublicId, actor),
                     (active.PublicId, administrator),
                     (deleting.PublicId, actor),
                     (deleted.PublicId, actor),
                 })
        {
            using var response = await Api.SendAsync(HttpMethod.Delete, LeavePath(spaceId), caller.AccessToken);
            errors.Add(await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
        Assert.That((await GetSpaceMembershipsAsync(active.Id)).Single().Status, Is.EqualTo(SpaceMembershipStatus.Active));
    }
}
