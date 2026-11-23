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
public sealed class MembershipMutationEndpointTests : MembershipIntegrationTestBase
{
    [TestCase(SpaceMembershipStatus.Active, "ACTIVE")]
    [TestCase(SpaceMembershipStatus.Suspended, "SUSPENDED")]
    public async Task ReplaceRoles_ShouldReplaceEntireSetAndPreserveAssignmentHistory(
        SpaceMembershipStatus status,
        string expectedStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var manager = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, manager, owner, roleKeys: [SpaceRoleSystemKey.Admin]);
        var membership = await SeedMembershipAsync(
            space,
            target,
            owner,
            status,
            SpaceRoleSystemKey.Member,
            SpaceRoleSystemKey.Viewer);
        var originalLinks = await GetRoleLinksAsync(membership);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        var roles = await GetSpaceRolesAsync(space.Id);
        var memberRole = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Member);
        var adminRole = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Admin);
        const string etag = "\"v1\"";
        Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await ReplaceRolesAsync(path, manager, etag, memberRole.PublicId, adminRole.PublicId);
        var updated = await ReadMembershipAsync(response);
        AssertMembershipEtag(response, 2);

        var links = await GetRoleLinksAsync(membership);
        var stored = await GetMembershipAsync(membership);
        Assert.Multiple(() =>
        {
            Assert.That(updated.MembershipPublicId, Is.EqualTo(membership.PublicId));
            Assert.That(updated.User.UserPublicId, Is.EqualTo(target.PublicId));
            Assert.That(updated.Status, Is.EqualTo(expectedStatus));
            Assert.That(updated.JoinedAt, Is.EqualTo(membership.JoinedAt));
            Assert.That(updated.UpdatedAt, Is.GreaterThan(membership.UpdatedAt));
            Assert.That(updated.Roles.Select(role => (role.RolePublicId, role.Code, role.Name)), Is.EquivalentTo(new[]
            {
                (memberRole.PublicId, "MEMBER", memberRole.Name),
                (adminRole.PublicId, "ADMIN", adminRole.Name),
            }));
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(stored.SuspendedAt, Is.EqualTo(membership.SuspendedAt));
            Assert.That(stored.Version, Is.EqualTo(2));
            Assert.That(stored.UpdatedAt, Is.EqualTo(updated.UpdatedAt));
            Assert.That(links.Select(link => link.RoleId), Is.EquivalentTo(new[] { memberRole.Id, adminRole.Id }));
            Assert.That(
                links.Single(link => link.RoleId == memberRole.Id),
                Is.EqualTo(originalLinks.Single(link => link.RoleId == memberRole.Id)));
            var newLink = links.Single(link => link.RoleId == adminRole.Id);
            Assert.That(newLink.AssignedByUserId, Is.EqualTo(manager.Id));
            Assert.That(newLink.AssignedAt, Is.EqualTo(stored.UpdatedAt));
        });

        using var stale = await ReplaceRolesAsync(path, manager, etag, adminRole.PublicId, memberRole.PublicId);
        await IntegrationAssert.AssertErrorAsync(stale, HttpStatusCode.PreconditionFailed, "PRECONDITION_FAILED");
        await AssertStoredMembershipAsync(stored, links);
    }

    [Test]
    public async Task ReplaceRoles_WithListedRoleAndDetailEtag_ShouldReturnUpdatedMembership()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var listResponse = await Api.GetAsync(RolesPath(space.PublicId), owner.AccessToken);
        var roles = await ReadRolesAsync(listResponse);
        var viewer = roles.Single(role => role.Code == "VIEWER");
        using var detail = await Api.GetAsync(path, owner.AccessToken);
        var before = await ReadMembershipAsync(detail);
        var etag = AssertMembershipEtag(detail, membership.Version);

        using var response = await ReplaceRolesAsync(path, owner, etag, viewer.RolePublicId);
        var updated = await ReadMembershipAsync(response);
        AssertMembershipEtag(response, membership.Version + 1);
        Assert.That(updated.MembershipPublicId, Is.EqualTo(before.MembershipPublicId));
        Assert.That(updated.Roles.Select(role => role.RolePublicId), Is.EqualTo(new[] { viewer.RolePublicId }));
    }

    [Test]
    public async Task ReplaceRoles_WithReorderedCurrentSet_ShouldKeepVersionAndTimestamps()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(
            space,
            target,
            owner,
            roleKeys: [SpaceRoleSystemKey.Member, SpaceRoleSystemKey.Viewer]);
        var originalLinks = await GetRoleLinksAsync(membership);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var detailResponse = await Api.GetAsync(path, owner.AccessToken);
        var before = await ReadMembershipAsync(detailResponse);
        var etag = AssertMembershipEtag(detailResponse, membership.Version);
        var reversedRoleIds = before.Roles.Reverse().Select(role => role.RolePublicId).ToArray();
        Assert.That(reversedRoleIds, Has.Length.EqualTo(2));
        Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await ReplaceRolesAsync(path, owner, etag, reversedRoleIds);
        var after = await ReadMembershipAsync(response);
        AssertMembershipEtag(response, membership.Version);
        Assert.Multiple(() =>
        {
            Assert.That(after.Status, Is.EqualTo(before.Status));
            Assert.That(after.UpdatedAt, Is.EqualTo(before.UpdatedAt));
            Assert.That(after.Roles, Is.EquivalentTo(before.Roles));
        });
        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [Test]
    public async Task ReplaceRoles_WithMembersManage_ShouldAllowChangingOwnRoles()
    {
        var owner = await RegisterSpaceUserAsync();
        var manager = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, manager, owner, roleKeys: [SpaceRoleSystemKey.Admin]);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var path = MembershipPath(space.PublicId, membership.PublicId);

        using var response = await ReplaceRolesAsync(path, manager, "\"v1\"", viewer.PublicId);
        var updated = await ReadMembershipAsync(response);
        AssertMembershipEtag(response, 2);
        Assert.That(updated.Roles.Select(role => role.RolePublicId), Is.EqualTo(new[] { viewer.PublicId }));

        using var readAfter = await Api.GetAsync(path, manager.AccessToken);
        await IntegrationAssert.AssertErrorAsync(readAfter, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        Assert.That((await GetRoleLinksAsync(membership)).Select(link => link.RoleId), Is.EqualTo(new[] { viewer.Id }));
    }

    [TestCase("{}")]
    [TestCase("{\"rolePublicIds\":null}")]
    [TestCase("{\"rolePublicIds\":{}}")]
    [TestCase("{\"rolePublicIds\":[]}")]
    [TestCase("{\"rolePublicIds\":[1]}")]
    [TestCase("{\"rolePublicIds\":[\"01977b5b-164c-73b8-b681-1eabcc05a444\",\"01977b5b-164c-73b8-b681-1eabcc05a444\"]}")]
    [TestCase("{\"rolePublicIds\":[\"c8f447e7-4701-4b78-9eb9-d46262d5b313\"]}")]
    [TestCase("{\"rolePublicIds\":[\"01977b5b-164c-73b8-7681-1eabcc05a444\"]}")]
    [TestCase("{\"rolePublicIds\":[\"invalid\"]}")]
    public async Task ReplaceRoles_WithInvalidRolePublicIds_ShouldReturnFieldError(string body)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner);
        var originalLinks = await GetRoleLinksAsync(membership);

        using var response = await Api.SendRawAsync(
            HttpMethod.Put,
            $"{MembershipPath(space.PublicId, membership.PublicId)}/roles",
            body,
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v1\"");
        var error = await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        Assert.That(
            error.Error.Details.Select(detail => (detail.Field, detail.Code)),
            Is.EqualTo(new[] { ("rolePublicIds", "MEMBER_INVALID_ROLE") }));
        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [TestCase("{", "application/json", HttpStatusCode.BadRequest)]
    [TestCase("{\"rolePublicIds\":[\"ROLE\"],\"unexpected\":true}", "application/json", HttpStatusCode.BadRequest)]
    [TestCase("{\"rolePublicIds\":[\"ROLE\"]}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task ReplaceRoles_WithInvalidHttpBody_ShouldUseStandardErrorEnvelope(
        string body,
        string mediaType,
        HttpStatusCode expectedStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner);
        var originalLinks = await GetRoleLinksAsync(membership);
        var rolePublicId = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer).PublicId;

        using var response = await Api.SendRawAsync(
            HttpMethod.Put,
            $"{MembershipPath(space.PublicId, membership.PublicId)}/roles",
            body.Replace("ROLE", rolePublicId.ToString("D"), StringComparison.Ordinal),
            mediaType,
            owner.AccessToken,
            ifMatch: "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(response, expectedStatus, "REQUEST_VALIDATION_FAILED");
        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [Test]
    public async Task ReplaceRoles_ShouldMaskIneligibleRolesAndRejectWholeSet()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var foreignSpace = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner);
        var originalLinks = await GetRoleLinksAsync(membership);
        var roles = await GetSpaceRolesAsync(space.Id);
        var admin = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Admin);
        var viewer = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var foreignRole = (await GetSpaceRolesAsync(foreignSpace.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        var customRoleId = Guid.CreateVersion7();
        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.SpaceRoles.Where(role => role.Id == viewer.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.DeletedAt, viewer.UpdatedAt));
            context.SpaceRoles.Add(new SpaceRoleEntity
            {
                PublicId = customRoleId,
                SpaceId = space.Id,
                Name = "Custom role",
                NormalizedName = "CUSTOM ROLE",
                IsManaged = false,
                Version = 1,
                CreatedAt = viewer.CreatedAt,
                UpdatedAt = viewer.UpdatedAt,
            });
            await context.SaveChangesAsync();
        }

        var errors = new List<ErrorEnvelope>();
        foreach (var ineligibleRoleId in new[] { Guid.CreateVersion7(), foreignRole.PublicId, viewer.PublicId, customRoleId })
        {
            using var response = await ReplaceRolesAsync(path, owner, "\"v1\"", admin.PublicId, ineligibleRoleId);
            errors.Add(await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [Test]
    public async Task SuspendMember_ShouldPreserveAssignmentsAndRevokeAccess()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner);
        var originalLinks = await GetRoleLinksAsync(membership);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var accessBefore = await Api.GetAsync(SpacePath(space.PublicId), target.AccessToken);
        await ReadSpaceAsync(accessBefore);
        Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/suspend", owner, "\"v1\"");
        var suspended = await ReadMembershipAsync(response);
        var etag = AssertMembershipEtag(response, 2);
        var stored = await GetMembershipAsync(membership);
        Assert.Multiple(() =>
        {
            Assert.That(suspended.Status, Is.EqualTo("SUSPENDED"));
            Assert.That(suspended.Roles.Select(role => role.Code), Is.EqualTo(new[] { "MEMBER" }));
            Assert.That(stored.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));
            Assert.That(stored.Version, Is.EqualTo(2));
            Assert.That(stored.UpdatedAt, Is.EqualTo(suspended.UpdatedAt).And.GreaterThan(membership.UpdatedAt));
            Assert.That(stored.SuspendedAt, Is.EqualTo(stored.UpdatedAt));
        });
        Assert.That(await GetRoleLinksAsync(membership), Is.EqualTo(originalLinks));

        using var accessAfter = await Api.GetAsync(SpacePath(space.PublicId), target.AccessToken);
        await IntegrationAssert.AssertErrorAsync(accessAfter, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        using var stale = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/suspend", owner, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(stale, HttpStatusCode.PreconditionFailed, "PRECONDITION_FAILED");
        using var repeated = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/suspend", owner, etag);
        await IntegrationAssert.AssertErrorAsync(repeated, HttpStatusCode.Conflict, "MEMBER_INVALID_STATE");
        await AssertStoredMembershipAsync(stored, originalLinks);
    }

    [Test]
    public async Task ActivateMember_ShouldPreserveAssignmentsAndRestoreAccess()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner, SpaceMembershipStatus.Suspended);
        var originalLinks = await GetRoleLinksAsync(membership);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var accessBefore = await Api.GetAsync(SpacePath(space.PublicId), target.AccessToken);
        await IntegrationAssert.AssertErrorAsync(accessBefore, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/activate", owner, "\"v1\"");
        var activated = await ReadMembershipAsync(response);
        var etag = AssertMembershipEtag(response, 2);
        var stored = await GetMembershipAsync(membership);
        Assert.Multiple(() =>
        {
            Assert.That(activated.Status, Is.EqualTo("ACTIVE"));
            Assert.That(activated.Roles.Select(role => role.Code), Is.EqualTo(new[] { "MEMBER" }));
            Assert.That(stored.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(stored.Version, Is.EqualTo(2));
            Assert.That(stored.SuspendedAt, Is.Null);
            Assert.That(stored.UpdatedAt, Is.EqualTo(activated.UpdatedAt).And.GreaterThan(membership.UpdatedAt));
        });
        Assert.That(await GetRoleLinksAsync(membership), Is.EqualTo(originalLinks));

        using var accessAfter = await Api.GetAsync(SpacePath(space.PublicId), target.AccessToken);
        await ReadSpaceAsync(accessAfter);

        using var repeated = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/activate", owner, etag);
        await IntegrationAssert.AssertErrorAsync(repeated, HttpStatusCode.Conflict, "MEMBER_INVALID_STATE");
        await AssertStoredMembershipAsync(stored, originalLinks);
    }

    [Test]
    public async Task ActivateMember_WithoutRoles_ShouldRequireRoleAssignmentFirst()
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner, SpaceMembershipStatus.Suspended);
        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.SpaceMembershipRoles.Where(link => link.MembershipId == membership.Id).ExecuteDeleteAsync();
        }

        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var rejected = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/activate", owner, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(rejected, HttpStatusCode.Conflict, "MEMBER_INVALID_ROLE");
        await AssertStoredMembershipAsync(membership, []);

        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        using var replacement = await ReplaceRolesAsync(path, owner, "\"v1\"", viewer.PublicId);
        var assigned = await ReadMembershipAsync(replacement);
        var etag = AssertMembershipEtag(replacement, 2);
        Assert.That(assigned.Status, Is.EqualTo("SUSPENDED"));

        using var activation = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/activate", owner, etag);
        var activated = await ReadMembershipAsync(activation);
        AssertMembershipEtag(activation, 3);
        Assert.That(activated.Status, Is.EqualTo("ACTIVE"));
        Assert.That(activated.Roles.Select(role => role.RolePublicId), Is.EqualTo(new[] { viewer.PublicId }));
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task RemoveMember_ShouldReturnNewEtagAndPreserveRoleHistory(SpaceMembershipStatus status)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner, status);
        var originalLinks = await GetRoleLinksAsync(membership);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await SendSpaceActionAsync(HttpMethod.Delete, path, owner, "\"v1\"");
        await AssertNoContentAsync(response);
        var etag = AssertMembershipEtag(response, 2);

        using var detail = await Api.GetAsync(path, owner.AccessToken);
        var removed = await ReadMembershipAsync(detail);
        AssertETag(detail, etag);
        var stored = await GetMembershipAsync(membership);
        Assert.Multiple(() =>
        {
            Assert.That(removed.Status, Is.EqualTo("LEFT"));
            Assert.That(removed.Roles.Select(role => role.Code), Is.EqualTo(new[] { "MEMBER" }));
            Assert.That(stored.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(stored.Version, Is.EqualTo(2));
            Assert.That(stored.SuspendedAt, Is.Null);
            Assert.That(stored.UpdatedAt, Is.EqualTo(removed.UpdatedAt).And.GreaterThan(membership.UpdatedAt));
            Assert.That(stored.LeftAt, Is.EqualTo(stored.UpdatedAt));
        });
        Assert.That(await GetRoleLinksAsync(membership), Is.EqualTo(originalLinks));
    }

    [TestCaseSource(nameof(IfMatchCases))]
    public async Task Mutations_WithMissingOrMalformedIfMatch_ShouldRejectWithoutWriting(string action, string? ifMatch)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var status = action == "activate" ? SpaceMembershipStatus.Suspended : SpaceMembershipStatus.Active;
        var membership = await SeedMembershipAsync(space, target, owner, status);
        var originalLinks = await GetRoleLinksAsync(membership);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var path = MembershipPath(space.PublicId, membership.PublicId);

        using var response = await SendMutationAsync(action, path, owner, ifMatch, viewer.PublicId);
        if (ifMatch is null)
        {
            await IntegrationAssert.AssertErrorAsync(response, (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        }
        else
        {
            var error = await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
            Assert.That(error.Error.Details.Select(detail => (detail.Field, detail.Code)),
                Is.EqualTo(new[] { ("ifMatch", "INVALID_ETAG") }));
        }

        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [TestCase("roles")]
    [TestCase("suspend")]
    [TestCase("activate")]
    [TestCase("remove")]
    public async Task Mutations_WithArchivedSpace_ShouldRejectWithoutWriting(string action)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);
        var status = action == "activate" ? SpaceMembershipStatus.Suspended : SpaceMembershipStatus.Active;
        var membership = await SeedMembershipAsync(space, target, owner, status);
        var originalLinks = await GetRoleLinksAsync(membership);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);

        using var response = await SendMutationAsync(
            action, MembershipPath(space.PublicId, membership.PublicId), owner, "\"v1\"", viewer.PublicId);
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "SPACE_ARCHIVED");
        await AssertStoredMembershipAsync(membership, originalLinks);
    }

    [TestCase("roles")]
    [TestCase("suspend")]
    [TestCase("activate")]
    [TestCase("remove")]
    public async Task Mutations_WithMissingForeignOrOwnerMembership_ShouldReturnSame404(string action)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        var status = action == "activate" ? SpaceMembershipStatus.Suspended : SpaceMembershipStatus.Active;
        var ownerMembership = await SeedMembershipAsync(space, owner, owner, status);
        var foreignMembership = await SeedMembershipAsync(otherSpace, target, owner, status);
        var ownerLinks = await GetRoleLinksAsync(ownerMembership);
        var foreignLinks = await GetRoleLinksAsync(foreignMembership);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);

        var errors = new List<ErrorEnvelope>();
        foreach (var publicId in new[] { Guid.CreateVersion7(), foreignMembership.PublicId, ownerMembership.PublicId })
        {
            using var response = await SendMutationAsync(action, MembershipPath(space.PublicId, publicId), owner, "\"v1\"", viewer.PublicId);
            errors.Add(await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
        await AssertStoredMembershipAsync(ownerMembership, ownerLinks);
        await AssertStoredMembershipAsync(foreignMembership, foreignLinks);
    }

    [TestCase("roles")]
    [TestCase("suspend")]
    [TestCase("activate")]
    [TestCase("remove")]
    public async Task Mutations_WithLeftMembership_ShouldCheckVersionBeforeState(string action)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, target, owner, SpaceMembershipStatus.Left);
        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.SpaceMemberships.Where(row => row.Id == membership.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, 2L));
        }

        var stored = await GetMembershipAsync(membership);
        var originalLinks = await GetRoleLinksAsync(membership);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var path = MembershipPath(space.PublicId, membership.PublicId);
        using var stale = await SendMutationAsync(action, path, owner, "\"v1\"", viewer.PublicId);
        await IntegrationAssert.AssertErrorAsync(stale, HttpStatusCode.PreconditionFailed, "PRECONDITION_FAILED");

        using var fresh = await SendMutationAsync(action, path, owner, "\"v2\"", viewer.PublicId);
        await IntegrationAssert.AssertErrorAsync(fresh, HttpStatusCode.Conflict, "MEMBER_INACTIVE");
        await AssertStoredMembershipAsync(stored, originalLinks);
    }

    private static IEnumerable<TestCaseData> IfMatchCases()
    {
        foreach (var action in new[] { "roles", "suspend", "activate", "remove" })
        {
            foreach (var ifMatch in new[] { null, "W/\"v1\"", "*", "\"v1\", \"v2\"", "\"v0\"", "v1" })
            {
                yield return new TestCaseData(action, ifMatch);
            }
        }
    }

    private Task<HttpResponseMessage> ReplaceRolesAsync(
        string path,
        SpaceTestUser actor,
        string? ifMatch,
        params Guid[] rolePublicIds) =>
        Api.SendJsonAsync(HttpMethod.Put, $"{path}/roles", new { rolePublicIds }, actor.AccessToken, ifMatch);

    private Task<HttpResponseMessage> SendMutationAsync(
        string action,
        string path,
        SpaceTestUser actor,
        string? ifMatch,
        Guid rolePublicId) =>
        action switch
        {
            "roles" => ReplaceRolesAsync(path, actor, ifMatch, rolePublicId),
            "remove" => SendSpaceActionAsync(HttpMethod.Delete, path, actor, ifMatch),
            _ => SendSpaceActionAsync(HttpMethod.Post, $"{path}/{action}", actor, ifMatch),
        };

    private async Task<SpaceMembershipEntity> GetMembershipAsync(SpaceMembershipEntity membership) =>
        (await GetSpaceMembershipsAsync(membership.SpaceId)).Single(row => row.Id == membership.Id);

    private async Task<MembershipRoleLink[]> GetRoleLinksAsync(SpaceMembershipEntity membership) =>
        (await GetSpaceMembershipRoleLinksAsync(membership.SpaceId))
            .Where(link => link.MembershipId == membership.Id)
            .OrderBy(link => link.RoleId)
            .Select(link => new MembershipRoleLink(link.RoleId, link.AssignedByUserId, link.AssignedAt))
            .ToArray();

    private async Task AssertStoredMembershipAsync(SpaceMembershipEntity expected, MembershipRoleLink[] expectedLinks)
    {
        var actual = await GetMembershipAsync(expected);
        var actualLinks = await GetRoleLinksAsync(expected);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.Version, Is.EqualTo(expected.Version));
            Assert.That(actual.JoinedAt, Is.EqualTo(expected.JoinedAt));
            Assert.That(actual.UpdatedAt, Is.EqualTo(expected.UpdatedAt));
            Assert.That(actual.SuspendedAt, Is.EqualTo(expected.SuspendedAt));
            Assert.That(actual.LeftAt, Is.EqualTo(expected.LeftAt));
            Assert.That(actualLinks, Is.EqualTo(expectedLinks));
        });
    }

    private sealed record MembershipRoleLink(long RoleId, long AssignedByUserId, DateTimeOffset AssignedAt);
}
