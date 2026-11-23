using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MembershipReadEndpointTests : MembershipIntegrationTestBase
{
    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ListMemberships_ShouldIncludeCurrentAndPastMembershipsInTheTargetSpace(
        SpaceStatus spaceStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var rejoinedUser = await RegisterSpaceUserAsync();
        var suspendedUser = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: spaceStatus);
        var otherSpace = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, owner, owner);
        await SeedMembershipAsync(otherSpace, rejoinedUser, owner);

        var suspended = await SeedMembershipAsync(space, suspendedUser, owner, SpaceMembershipStatus.Suspended);
        Clock.Advance(TimeSpan.FromSeconds(1));
        var past = await SeedMembershipAsync(space, rejoinedUser, owner, SpaceMembershipStatus.Left);
        Clock.Advance(TimeSpan.FromSeconds(1));
        var current = await SeedMembershipAsync(space, rejoinedUser, owner);
        await UpdateMembershipAsync(suspended.Id, membership =>
            membership.UpdatedAt = current.UpdatedAt.AddSeconds(1));

        var path = MembershipsPath(space.PublicId);
        using var defaultResponse = await Api.GetAsync(path, owner.AccessToken);
        var defaultPage = await ReadMembershipPageAsync(defaultResponse);
        var memberRole = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Member);
        Assert.Multiple(() =>
        {
            Assert.That(defaultPage.Items.Select(item => (item.MembershipPublicId, item.User.UserPublicId, item.Status)),
                Is.EqualTo(new[]
                {
                    (current.PublicId, rejoinedUser.PublicId, "ACTIVE"),
                    (past.PublicId, rejoinedUser.PublicId, "LEFT"),
                    (suspended.PublicId, suspendedUser.PublicId, "SUSPENDED"),
                }));
            foreach (var item in defaultPage.Items)
            {
                Assert.That(item.Roles.Select(role => (role.RolePublicId, role.Code, role.Name)),
                    Is.EqualTo(new[] { (memberRole.PublicId, "MEMBER", memberRole.Name) }));
            }

            Assert.That(defaultPage.NextCursor, Is.Null);
        });

        using var sortedResponse = await Api.GetAsync($"{path}?sort=UpDaTeDaT&direction=ASC", owner.AccessToken);
        var sortedPage = await ReadMembershipPageAsync(sortedResponse);
        Assert.That(sortedPage.Items.Select(item => item.MembershipPublicId),
            Is.EqualTo(new[] { past.PublicId, current.PublicId, suspended.PublicId }));
    }

    [Test]
    public async Task ListMemberships_WithNoNonOwnerMemberships_ShouldReturnEmptyPage()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, owner, owner);

        using var response = await Api.GetAsync(MembershipsPath(space.PublicId), owner.AccessToken);
        var page = await ReadMembershipPageAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(page.NextCursor, Is.Null);
        });
    }

    [TestCase("", 50)]
    [TestCase("?limit=100", 100)]
    public async Task ListMemberships_ShouldApplyRequestedOrDefaultPageLimit(string query, int limit)
    {
        var owner = await RegisterSpaceUserAsync();
        var formerMember = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var now = space.CreatedAt;

        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.SpaceMemberships.AddRange(Enumerable.Range(0, 101).Select(_ => new SpaceMembershipEntity
            {
                PublicId = Guid.CreateVersion7(),
                SpaceId = space.Id,
                UserId = formerMember.Id,
                Status = SpaceMembershipStatus.Left,
                Version = 1,
                JoinedAt = now,
                UpdatedAt = now,
                LeftAt = now,
            }));
            await context.SaveChangesAsync();
        }

        using var response = await Api.GetAsync($"{MembershipsPath(space.PublicId)}{query}", owner.AccessToken);
        var page = await ReadMembershipPageAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Count.EqualTo(limit));
            Assert.That(page.Items.Select(item => item.MembershipPublicId), Is.Unique);
            Assert.That(page.Items.Select(item => item.Roles), Is.All.Empty);
            Assert.That(page.NextCursor, Is.Not.Null.And.Not.Empty);
        });
    }

    [TestCase("joinedAt", "ASC")]
    [TestCase("updatedAt", "DESC")]
    public async Task ListMemberships_WithEqualTimestamps_ShouldPageByPublicId(string sort, string direction)
    {
        var owner = await RegisterSpaceUserAsync();
        var first = await RegisterSpaceUserAsync();
        var second = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var memberships = new[]
        {
            await SeedMembershipAsync(space, first, owner, SpaceMembershipStatus.Left),
            await SeedMembershipAsync(space, first, owner, roleKeys: [SpaceRoleSystemKey.Member, SpaceRoleSystemKey.Viewer]),
            await SeedMembershipAsync(space, second, owner),
        };
        var ids = memberships.Select(membership => membership.PublicId.ToString("D"));
        var expected = direction == "ASC"
            ? ids.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : ids.OrderByDescending(id => id, StringComparer.Ordinal).ToArray();
        var path = $"{MembershipsPath(space.PublicId)}?limit=1&sort={sort}&direction={direction}";

        string? cursor = null;
        for (var index = 0; index < expected.Length; index++)
        {
            var cursorQuery = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await Api.GetAsync(path + cursorQuery, owner.AccessToken);
            var page = await ReadMembershipPageAsync(response);

            Assert.That(page.Items.Select(item => item.MembershipPublicId.ToString("D")),
                Is.EqualTo(new[] { expected[index] }));
            cursor = page.NextCursor;
            if (index == expected.Length - 1)
            {
                Assert.That(cursor, Is.Null);
            }
            else
            {
                Assert.That(cursor, Does.Match("^[A-Za-z0-9_-]+$"));
            }
        }
    }

    [Test]
    public async Task ListMemberships_WithTamperedOrOutOfScopeCursor_ShouldRejectIt()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var otherActor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, actor, owner);
        await SeedMembershipAsync(space, otherActor, owner);
        await SeedMembershipAsync(otherSpace, actor, owner);

        var path = MembershipsPath(space.PublicId);
        using var first = await Api.GetAsync($"{path}?limit=1", actor.AccessToken);
        var page = await ReadMembershipPageAsync(first);
        Assert.That(page.NextCursor, Does.Match("^[A-Za-z0-9_-]+$"));
        var cursor = page.NextCursor!;
        var tamperIndex = cursor.Length / 2;
        var replacement = cursor[tamperIndex] == 'A' ? 'B' : 'A';
        var tamperedCursor = $"{cursor[..tamperIndex]}{replacement}{cursor[(tamperIndex + 1)..]}";
        var cursorQuery = $"limit=1&cursor={Uri.EscapeDataString(cursor)}";

        await AssertCursorRejectedAsync($"{path}?cursor={tamperedCursor}", actor);
        await AssertCursorRejectedAsync($"{path}?{cursorQuery}", otherActor);
        await AssertCursorRejectedAsync($"{MembershipsPath(otherSpace.PublicId)}?{cursorQuery}", actor);
        await AssertCursorRejectedAsync($"{path}?{cursorQuery}&sort=updatedAt", actor);
        await AssertCursorRejectedAsync($"{path}?{cursorQuery}&direction=ASC", actor);

        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedSpace = await context.Spaces.SingleAsync(candidate => candidate.Id == space.Id);
        persistedSpace.OwnerUserId = otherActor.Id;
        await context.SaveChangesAsync();

        await AssertCursorRejectedAsync($"{path}?{cursorQuery}", actor);
    }

    [TestCase("cursor=", "CURSOR_INVALID")]
    [TestCase("limit=0", "REQUEST_VALIDATION_FAILED")]
    [TestCase("limit=101", "REQUEST_VALIDATION_FAILED")]
    [TestCase("limit=one", "REQUEST_VALIDATION_FAILED")]
    [TestCase("sort=name", "REQUEST_VALIDATION_FAILED")]
    [TestCase("direction=sideways", "REQUEST_VALIDATION_FAILED")]
    public async Task ListMemberships_WithInvalidQuery_ShouldReturnBadRequest(string query, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await Api.GetAsync($"{MembershipsPath(space.PublicId)}?{query}", owner.AccessToken);

        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
    }

    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Active, "ACTIVE", "member", null)]
    [TestCase(SpaceStatus.Archived, SpaceMembershipStatus.Suspended, "SUSPENDED", null, "Suspended member")]
    [TestCase(SpaceStatus.Active, SpaceMembershipStatus.Left, "LEFT", null, null)]
    public async Task GetMembership_ShouldReturnDetailAndETag(
        SpaceStatus spaceStatus,
        SpaceMembershipStatus status,
        string statusCode,
        string? userName,
        string? displayName)
    {
        var owner = await RegisterSpaceUserAsync();
        var session = await RegisterAsync(TestData.NewRegistration(userName: userName, displayName: displayName));
        var userId = await GetUserIdAsync(session.User.UserPublicId);
        var subject = new SpaceTestUser(session.User.UserPublicId, userId, session.AccessToken);
        var space = await SeedSpaceAsync(owner, status: spaceStatus, version: 4);
        var membership = await SeedMembershipAsync(space, subject, owner, status, SpaceRoleSystemKey.Viewer);
        var viewerRole = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var updatedAt = membership.UpdatedAt.AddSeconds(2);
        const long version = 7;
        await UpdateMembershipAsync(membership.Id, persisted =>
        {
            persisted.Version = version;
            persisted.UpdatedAt = updatedAt;
        });

        using var response = await Api.GetAsync(MembershipPath(space.PublicId, membership.PublicId), owner.AccessToken);
        var detail = await ReadMembershipAsync(response);
        AssertMembershipEtag(response, version);

        Assert.Multiple(() =>
        {
            Assert.That(detail.MembershipPublicId, Is.EqualTo(membership.PublicId));
            Assert.That(detail.User.UserPublicId, Is.EqualTo(subject.PublicId));
            Assert.That(detail.User.UserName, Is.EqualTo(userName));
            Assert.That(detail.User.DisplayName, Is.EqualTo(displayName));
            Assert.That(detail.Status, Is.EqualTo(statusCode));
            Assert.That(detail.Roles.Select(role => (role.RolePublicId, role.Code, role.Name)),
                Is.EqualTo(new[] { (viewerRole.PublicId, "VIEWER", viewerRole.Name) }));
            Assert.That(detail.JoinedAt, Is.EqualTo(membership.JoinedAt));
            Assert.That(detail.UpdatedAt, Is.EqualTo(updatedAt));
        });
    }

    [Test]
    public async Task GetMembership_ForMissingForeignOrOwnerMembership_ShouldReturnSameNotFound()
    {
        var owner = await RegisterSpaceUserAsync();
        var subject = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        var ownerMembership = await SeedMembershipAsync(space, owner, owner);
        var foreignMembership = await SeedMembershipAsync(otherSpace, subject, owner);
        var errors = new List<ErrorEnvelope>();

        foreach (var publicId in new[] { Guid.CreateVersion7(), foreignMembership.PublicId, ownerMembership.PublicId })
        {
            using var response = await Api.GetAsync(
                MembershipPath(space.PublicId, publicId), owner.AccessToken, NewCorrelationId());
            errors.Add(await IntegrationAssert.AssertErrorAsync(
                response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
    }

    private async Task AssertCursorRejectedAsync(string path, SpaceTestUser actor)
    {
        using var response = await Api.GetAsync(path, actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    private async Task UpdateMembershipAsync(long id, Action<SpaceMembershipEntity> update)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var membership = await context.SpaceMemberships.SingleAsync(candidate => candidate.Id == id);
        update(membership);
        await context.SaveChangesAsync();
    }
}
