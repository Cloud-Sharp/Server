using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.Entities;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InviteDetailEndpointTests : InviteIntegrationTestBase
{
    [TestCase(SpaceStatus.Active, SpaceInviteStatus.Active, 7L)]
    [TestCase(SpaceStatus.Active, SpaceInviteStatus.Revoked, 8L)]
    [TestCase(SpaceStatus.Active, SpaceInviteStatus.Expired, 9L)]
    [TestCase(SpaceStatus.Archived, SpaceInviteStatus.Active, 10L)]
    public async Task GetInvite_ShouldReturnStoredManagementFieldsAndETag(
        SpaceStatus spaceStatus,
        SpaceInviteStatus inviteStatus,
        long version)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: spaceStatus, version: 3);
        var invite = await SeedInviteAsync(
            space,
            owner,
            inviteStatus,
            maxUses: 2,
            useCount: inviteStatus == SpaceInviteStatus.Expired ? 2 : 1,
            version: version,
            createdAt: space.CreatedAt.AddMinutes(-1));

        using var response = await Api.GetAsync(InviteDetailPath(space.PublicId, invite.Row.PublicId), owner.AccessToken);

        await AssertInviteDetailAsync(response, invite.Row);
    }

    [Test]
    public async Task GetInvite_ShouldMaskMissingAndCrossSpaceInvitesWithTheSameError()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        var foreign = await SeedInviteAsync(otherSpace, owner);

        using var missing = await Api.GetAsync(
            InviteDetailPath(space.PublicId, Guid.CreateVersion7()), owner.AccessToken, NewCorrelationId());
        using var crossSpace = await Api.GetAsync(
            InviteDetailPath(space.PublicId, foreign.Row.PublicId), owner.AccessToken, NewCorrelationId());

        var missingError = await IntegrationAssert.AssertErrorAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var crossSpaceError = await IntegrationAssert.AssertErrorAsync(crossSpace, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertSamePublicError(missingError, crossSpaceError);
    }

    [TestCase("not-a-uuid", "spacePublicId", "SPACE_PUBLIC_ID_INVALID")]
    [TestCase("not-a-uuid", "invitePublicId", "INVITE_PUBLIC_ID_INVALID")]
    [TestCase("018f2f6e-8a9a-4c3a-9d7a-3f4b5a6b7c8d", "invitePublicId", "INVITE_PUBLIC_ID_INVALID")]
    [TestCase("018f2f6e-8a9a-7c3a-7d7a-3f4b5a6b7c8d", "invitePublicId", "INVITE_PUBLIC_ID_INVALID")]
    public async Task GetInvite_ShouldRejectInvalidPublicIds(string invalidId, string field, string detailCode)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner);
        var path = field == "spacePublicId"
            ? $"{SpacesPath}/{invalidId}/invites/{invite.Row.PublicId:D}"
            : $"{InviteListPath(space.PublicId)}/{invalidId}";

        using var response = await Api.GetAsync(path, owner.AccessToken);
        var error = await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");

        Assert.That(error.Error.Details.Select(detail => (detail.Field, detail.Code)), Does.Contain((field, detailCode)));
    }

    [Test]
    public async Task GetInvite_ShouldRequireAUserSessionAndInviteManagementPermission()
    {
        await AssertInviteManagementAuthorizationAsync((actor, space, invitePublicId) =>
            Api.GetAsync(InviteDetailPath(space.PublicId, invitePublicId), actor?.AccessToken));
    }

    private static async Task AssertInviteDetailAsync(HttpResponseMessage response, SpaceInviteEntity expected)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);

        AssertInviteManagementItem(document.RootElement, expected);
        AssertETag(response, FormattableString.Invariant($"\"v{expected.Version}\""));
    }
}
