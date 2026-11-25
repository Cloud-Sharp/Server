using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InvitePublicInfoEndpointTests : InviteIntegrationTestBase
{
    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task GetPublicInfo_Anonymously_ShouldReturnOnlyPublicFieldsWithoutConsumingASlot(SpaceStatus status)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, name: "Shared workspace", status: status);
        var invite = await SeedInviteAsync(space, owner, maxUses: 1);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await Api.GetAsync(InvitePublicPath(invite.Token));

            var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
            AssertCapabilityHeaders(response);
            using var document = JsonDocument.Parse(body);
            var info = document.RootElement;
            IntegrationAssert.AssertPropertyNames(info, "spaceName", "roleName", "expiresAt");
            var expiresAt = info.GetProperty("expiresAt").GetDateTimeOffset();
            IntegrationAssert.AssertUtc(expiresAt, "expiresAt");
            IntegrationAssert.AssertDoesNotContain(body, invite.Token, "Public info must not disclose the invite token.");
            Assert.Multiple(() =>
            {
                Assert.That(info.GetProperty("spaceName").GetString(), Is.EqualTo(space.Name));
                Assert.That(info.GetProperty("roleName").GetString(), Is.EqualTo(viewer.Name));
                Assert.That(expiresAt, Is.EqualTo(invite.Row.ExpiresAt));
            });
        }

        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
        Assert.That(await GetSpaceMembershipsAsync(space.Id), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InviteCapability_OverPlainHttp_ShouldReturnOpaqueNotFoundWithoutRedirect(bool accept)
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = accept ? await RegisterSpaceUserAsync() : null;
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner, maxUses: 1);
        var path = accept ? InviteAcceptancePath(invite.Token) : InvitePublicPath(invite.Token);
        var unknownToken = NewInviteToken();
        var unknownPath = accept ? InviteAcceptancePath(unknownToken) : InvitePublicPath(unknownToken);
        var method = accept ? HttpMethod.Post : HttpMethod.Get;

        using var plainHttp = await Api.SendAsync(
            method, $"http://localhost{path}", actor?.AccessToken,
            configure: accept ? WithIdempotencyKey(NewIdempotencyKey()) : null);
        var plainHttpError = await IntegrationAssert.AssertErrorAsync(plainHttp, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertCapabilityHeaders(plainHttp);
        Assert.That(plainHttp.Headers.Contains("Location"), Is.False);
        IntegrationAssert.AssertDoesNotContain(
            await TestApiClient.ReadBodyAsync(plainHttp), invite.Token, "The HTTP response must not disclose the capability.");

        using var unknown = await Api.SendAsync(
            method, unknownPath, actor?.AccessToken,
            configure: accept ? WithIdempotencyKey(NewIdempotencyKey()) : null);
        var unknownError = await IntegrationAssert.AssertErrorAsync(unknown, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertSamePublicError(unknownError, plainHttpError);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
        Assert.That(await GetSpaceMembershipsAsync(space.Id), Is.Empty);
        Assert.That(await GetSpaceMembershipRoleLinksAsync(space.Id), Is.Empty);
    }

    [Test]
    public async Task GetPublicInfo_WhenCapabilityIsUnusable_ShouldReturnSame404()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var deleting = await SeedSpaceAsync(owner, status: SpaceStatus.Deleting);
        var deleted = await SeedSpaceAsync(owner, status: SpaceStatus.Deleted);
        var noViewer = await SeedSpaceAsync(owner);
        await RemoveViewerRoleAsync(noViewer.Id);

        var revoked = await SeedInviteAsync(space, owner, status: SpaceInviteStatus.Revoked);
        var exhausted = await SeedInviteAsync(space, owner, status: SpaceInviteStatus.Expired, maxUses: 1, useCount: 1);
        var expiresAtBoundary = await SeedInviteAsync(space, owner, expiresAt: Clock.UtcNow.AddMinutes(1));
        var inviteForDeleting = await SeedInviteAsync(deleting, owner);
        var inviteForDeleted = await SeedInviteAsync(deleted, owner);
        var inviteWithoutViewer = await SeedInviteAsync(noViewer, owner);
        Clock.Set(expiresAtBoundary.Row.ExpiresAt);

        var unregistered = NewInviteToken();
        var errors = new List<ErrorEnvelope>();
        foreach (var token in new[]
                 {
                     unregistered,
                     "CS_INV_" + unregistered["cs_inv_".Length..],
                     unregistered[..^1],
                     unregistered[..^1] + "!",
                     revoked.Token,
                     exhausted.Token,
                     expiresAtBoundary.Token,
                     inviteForDeleting.Token,
                     inviteForDeleted.Token,
                     inviteWithoutViewer.Token,
                 })
        {
            using var response = await Api.GetAsync(InvitePublicPath(token));
            errors.Add(await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
            AssertCapabilityHeaders(response);
            IntegrationAssert.AssertDoesNotContain(
                await TestApiClient.ReadBodyAsync(response),
                token,
                "The error must not disclose the capability from the URL.");
        }

        AssertSamePublicError(errors.ToArray());
        foreach (var invite in new[] { revoked, exhausted, expiresAtBoundary, inviteForDeleting, inviteForDeleted, inviteWithoutViewer })
        {
            var actual = (await GetInvitesAsync(invite.Row.SpaceId)).Single(row => row.Id == invite.Row.Id);
            AssertInviteUnchanged(actual, invite.Row);
        }
    }
}
