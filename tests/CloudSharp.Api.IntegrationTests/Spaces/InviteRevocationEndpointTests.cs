using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InviteRevocationEndpointTests : InviteIntegrationTestBase
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RevokeInvite_WithLatestIfMatch_ShouldUpdateStorageAndPreserveMembership(bool expiresByTime)
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, version: 6);
        var membership = await SeedMembershipAsync(space, member, owner,
            SpaceMembershipStatus.Active, SpaceRoleSystemKey.Viewer);
        var storedInvite = (await SeedInviteAsync(
            space,
            owner,
            expiresAt: Clock.UtcNow.AddDays(expiresByTime ? -1 : 1),
            maxUses: 3,
            useCount: 1,
            version: 3,
            createdAt: Clock.UtcNow.AddDays(-2))).Row;
        var membershipRole = (await GetSpaceMembershipRoleLinksAsync(space.Id)).Single();
        Clock.Advance(TimeSpan.FromMinutes(1));

        using var response = await SendRevokeInviteAsync(
            Api, space.PublicId, storedInvite.PublicId, owner.AccessToken, "\"v3\"");
        await AssertRevokedResponseAsync(response, "\"v4\"");

        var persisted = (await GetInvitesAsync(space.Id)).Single();
        var persistedMembership = (await GetSpaceMembershipsAsync(space.Id)).Single();
        var persistedMembershipRole = (await GetSpaceMembershipRoleLinksAsync(space.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Id, Is.EqualTo(storedInvite.Id));
            Assert.That(persisted.PublicId, Is.EqualTo(storedInvite.PublicId));
            Assert.That(persisted.Status, Is.EqualTo(SpaceInviteStatus.Revoked));
            Assert.That(persisted.Version, Is.EqualTo(4));
            Assert.That(persisted.RevokedAt, Is.EqualTo(ToPersistencePrecision(Clock.UtcNow)));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(persisted.RevokedAt));
            Assert.That(persisted.CreatedAt, Is.EqualTo(storedInvite.CreatedAt));
            Assert.That(persisted.ExpiresAt, Is.EqualTo(storedInvite.ExpiresAt));
            Assert.That(persisted.UseCount, Is.EqualTo(storedInvite.UseCount));
            Assert.That(persisted.MaxUses, Is.EqualTo(storedInvite.MaxUses));
            Assert.That(persistedMembership.PublicId, Is.EqualTo(membership.PublicId));
            Assert.That(persistedMembership.Version, Is.EqualTo(membership.Version));
            Assert.That(persistedMembership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(persistedMembershipRole.MembershipId, Is.EqualTo(membershipRole.MembershipId));
            Assert.That(persistedMembershipRole.RoleId, Is.EqualTo(membershipRole.RoleId));
        });
    }

    [Test]
    public async Task RevokeInvite_WithoutIfMatch_ShouldRequirePrecondition()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = (await SeedInviteAsync(space, owner)).Row;

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, null);
        await IntegrationAssert.AssertErrorAsync(response, (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite);
    }

    [TestCase("W/\"v1\"")]
    [TestCase("*")]
    [TestCase("\"v1\", \"v2\"")]
    [TestCase("\"v0\"")]
    [TestCase("v1")]
    public async Task RevokeInvite_WithMalformedIfMatch_ShouldReturnFieldError(string ifMatch)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = (await SeedInviteAsync(space, owner)).Row;

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, ifMatch);
        var error = await IntegrationAssert.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        Assert.That(error.Error.Details.Select(detail => (detail.Field, detail.Code)),
            Is.EqualTo(new[] { ("ifMatch", "INVALID_ETAG") }));
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite);
    }

    [Test]
    public async Task RevokeInvite_WithStaleIfMatch_ShouldLeaveCurrentInviteUnchanged()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = (await SeedInviteAsync(space, owner, version: 2)).Row;

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.PreconditionFailed, "PRECONDITION_FAILED");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite);
    }

    [Test]
    public async Task RevokeInvite_WithCompetingRequests_ShouldCommitOnlyOneChange()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = (await SeedInviteAsync(space, owner)).Row;
        using var otherApi = Environment.CreateClient();

        var firstTask = SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, "\"v1\"");
        var secondTask = SendRevokeInviteAsync(otherApi, space.PublicId, invite.PublicId, owner.AccessToken, "\"v1\"");
        using var first = await firstTask;
        using var second = await secondTask;

        Assert.That(new[] { first.StatusCode, second.StatusCode }, Is.EquivalentTo(
            new[] { HttpStatusCode.NoContent, HttpStatusCode.PreconditionFailed }));
        var successful = first.StatusCode == HttpStatusCode.NoContent ? first : second;
        var rejected = ReferenceEquals(successful, first) ? second : first;
        await AssertRevokedResponseAsync(successful, "\"v2\"");
        await IntegrationAssert.AssertErrorAsync(rejected, HttpStatusCode.PreconditionFailed, "PRECONDITION_FAILED");

        var persisted = (await GetInvitesAsync(space.Id)).Single();
        Assert.That(persisted.Status, Is.EqualTo(SpaceInviteStatus.Revoked));
        Assert.That(persisted.Version, Is.EqualTo(2));
    }

    [TestCase(SpaceInviteStatus.Revoked)]
    [TestCase(SpaceInviteStatus.Expired)]
    public async Task RevokeInvite_WhenStoredStateIsNotActive_ShouldReject(SpaceInviteStatus status)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = (await SeedInviteAsync(space, owner, status, maxUses: 1, useCount: 1, version: 2)).Row;

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, "\"v2\"");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "INVITE_INVALID_STATE");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite);
    }

    [Test]
    public async Task RevokeInvite_InArchivedSpace_ShouldRejectWithoutChangingInvite()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);
        var invite = (await SeedInviteAsync(space, owner)).Row;

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, invite.PublicId, owner.AccessToken, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "SPACE_ARCHIVED");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite);
    }

    [Test]
    public async Task RevokeInvite_WhenMissingOrInAnotherSpace_ShouldReturnSameNotFound()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        var otherInvite = (await SeedInviteAsync(otherSpace, owner)).Row;

        using var missing = await SendRevokeInviteAsync(Api, space.PublicId, Guid.CreateVersion7(), owner.AccessToken, "\"v1\"");
        using var crossSpace = await SendRevokeInviteAsync(Api, space.PublicId, otherInvite.PublicId, owner.AccessToken, "\"v1\"");
        var missingError = await IntegrationAssert.AssertErrorAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var crossSpaceError = await IntegrationAssert.AssertErrorAsync(crossSpace, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        AssertSamePublicError(missingError, crossSpaceError);
        Assert.That(await GetInvitesAsync(space.Id), Is.Empty);
        AssertInviteUnchanged((await GetInvitesAsync(otherSpace.Id)).Single(), otherInvite);
    }

    [Test]
    public async Task RevokeInvite_WithNonVersion7PublicId_ShouldReject()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await SendRevokeInviteAsync(Api, space.PublicId, Guid.NewGuid(), owner.AccessToken, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
    }

    [Test]
    public Task RevokeInvite_ShouldEnforceSessionAndInviteManagementPermission() =>
        AssertInviteManagementAuthorizationAsync((actor, space, invitePublicId) => SendRevokeInviteAsync(
            Api, space.PublicId, invitePublicId, actor?.AccessToken, "\"v1\""));

    private static Task<HttpResponseMessage> SendRevokeInviteAsync(
        TestApiClient api, Guid spacePublicId, Guid invitePublicId, string? token, string? ifMatch) =>
        api.SendAsync(HttpMethod.Delete, InviteDetailPath(spacePublicId, invitePublicId), token, configure: request =>
        {
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation(TestApiClient.IfMatchHeaderName, ifMatch);
            }
        });

    private static async Task AssertRevokedResponseAsync(HttpResponseMessage response, string etag)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        IntegrationAssert.AssertNoStore(response);
        AssertETag(response, etag);
        Assert.That(await TestApiClient.ReadBodyAsync(response), Is.Empty);
    }
}
