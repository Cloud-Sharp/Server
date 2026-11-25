using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InviteCreationEndpointTests : InviteIntegrationTestBase
{
    [TestCase("", null)]
    [TestCase(",\"maxUses\":null", null)]
    [TestCase(",\"maxUses\":1", 1)]
    [TestCase(",\"maxUses\":2147483647", int.MaxValue)]
    public async Task CreateInvite_ShouldReturnIssuanceContractAndPersistOnlyTheTokenHash(
        string maxUsesProperty,
        int? expectedMaxUses)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, version: 4);
        var expiresAt = ToPersistencePrecision(Clock.UtcNow.AddDays(1));
        var body = $$"""{"expiresAt":"{{expiresAt:O}}"{{maxUsesProperty}}}""";

        using var response = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, body, NewIdempotencyKey());
        var created = await ReadCreatedInviteAsync(response, space.PublicId);

        var persisted = (await GetInvitesAsync(space.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.ExpiresAt, Is.EqualTo(expiresAt));
            Assert.That(created.MaxUses, Is.EqualTo(expectedMaxUses));
            Assert.That(persisted.PublicId, Is.EqualTo(created.PublicId));
            Assert.That(persisted.SpaceId, Is.EqualTo(space.Id));
            Assert.That(persisted.CreatedByUserId, Is.EqualTo(owner.Id));
            Assert.That(persisted.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(persisted.UseCount, Is.Zero);
            Assert.That(persisted.MaxUses, Is.EqualTo(expectedMaxUses));
            Assert.That(persisted.ExpiresAt, Is.EqualTo(expiresAt));
            Assert.That(persisted.Version, Is.EqualTo(1));
            Assert.That(persisted.RevokedAt, Is.Null);
            Assert.That(
                string.Equals(persisted.TokenHash, Environment.TokenHasher.Hash(created.Token), StringComparison.Ordinal),
                Is.True,
                "The persisted token value must be the server-generated hash.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateInvite_WithCompletedKey_ShouldReturnOnlyTheOriginalPublicId(bool resetRedis)
    {
        var registration = TestData.NewRegistration();
        var owner = await RegisterSpaceUserAsync(registration);
        var space = await SeedSpaceAsync(owner);
        var body = ExpirationBody(Clock.UtcNow.AddMinutes(1));
        var key = NewIdempotencyKey();

        using var first = await SendCreateInviteAsync(space.PublicId, owner.AccessToken, body, key);
        var created = await ReadCreatedInviteAsync(first, space.PublicId);
        var initial = (await GetInvitesAsync(space.Id)).Single();

        var retryToken = owner.AccessToken;
        if (resetRedis)
        {
            retryToken = await ReauthenticateAfterRedisResetAsync(registration);
            Clock.Advance(TimeSpan.FromMinutes(2));
        }

        using var retry = await SendCreateInviteAsync(space.PublicId, retryToken, body, key);
        await IntegrationAssert.AssertErrorAsync(retry, HttpStatusCode.Conflict, "SECRET_RESPONSE_NOT_REPLAYABLE");
        var reply = await TestApiClient.ReadBodyAsync(retry);
        using var document = JsonDocument.Parse(reply);
        var root = document.RootElement;
        IntegrationAssert.AssertPropertyNames(root, "requestId", "error", "invitePublicId");
        Assert.That(root.GetProperty("invitePublicId").GetGuid(), Is.EqualTo(created.PublicId));
        IntegrationAssert.AssertDoesNotContain(reply, created.Token, "The retry body must not disclose the invite token.");
        var location = TestApiClient.GetHeader(retry, "Location") ?? string.Empty;
        IntegrationAssert.AssertDoesNotContain(location, created.Token, "The retry Location must not disclose the invite token.");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), initial);
    }

    [Test]
    public async Task CreateInvite_WithChangedRequestForCompletedKey_ShouldRejectReuse()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var key = NewIdempotencyKey();

        using var first = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(1)), key);
        await ReadCreatedInviteAsync(first, space.PublicId);
        var initial = (await GetInvitesAsync(space.Id)).Single();

        using var retry = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(2)), key);
        await IntegrationAssert.AssertErrorAsync(retry, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), initial);
    }

    [Test]
    public async Task CreateInvite_WithCompletedKey_ShouldCheckCurrentManagementPermission()
    {
        var owner = await RegisterSpaceUserAsync();
        var manager = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var managerMembership = await SeedMembershipAsync(space, manager, owner, roleKeys: [SpaceRoleSystemKey.Admin]);
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var body = ExpirationBody(Clock.UtcNow.AddDays(1));
        var key = NewIdempotencyKey();

        using var first = await SendCreateInviteAsync(space.PublicId, manager.AccessToken, body, key);
        await ReadCreatedInviteAsync(first, space.PublicId);
        var initial = (await GetInvitesAsync(space.Id)).Single();

        using var updated = await Api.SendJsonAsync(HttpMethod.Put,
            $"{SpacePath(space.PublicId)}/memberships/{managerMembership.PublicId:D}/roles",
            new { rolePublicIds = new[] { viewer.PublicId } }, owner.AccessToken, "\"v1\"");
        await ReadSuccessBodyAsync(updated, HttpStatusCode.OK);

        using var retry = await SendCreateInviteAsync(space.PublicId, manager.AccessToken, body, key);
        await IntegrationAssert.AssertErrorAsync(retry, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), initial);
    }

    [Test]
    public async Task CreateInvite_WithCompletedKey_ShouldReplayAfterArchiving()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var body = ExpirationBody(Clock.UtcNow.AddDays(1));
        var key = NewIdempotencyKey();

        using var first = await SendCreateInviteAsync(space.PublicId, owner.AccessToken, body, key);
        var created = await ReadCreatedInviteAsync(first, space.PublicId);
        using var archived = await SendSpaceActionAsync(HttpMethod.Post, $"{SpacePath(space.PublicId)}/archive", owner, "\"v1\"");
        await ReadSuccessBodyAsync(archived, HttpStatusCode.OK);

        using var retry = await SendCreateInviteAsync(space.PublicId, owner.AccessToken, body, key);
        await IntegrationAssert.AssertErrorAsync(retry, HttpStatusCode.Conflict, "SECRET_RESPONSE_NOT_REPLAYABLE");
        using var document = JsonDocument.Parse(await TestApiClient.ReadBodyAsync(retry));
        Assert.That(document.RootElement.GetProperty("invitePublicId").GetGuid(), Is.EqualTo(created.PublicId));
        Assert.That((await GetInvitesAsync(space.Id)).Single().PublicId, Is.EqualTo(created.PublicId));
    }

    [TestCase("""{}""", null)]
    [TestCase("""{"expiresAt":null}""", null)]
    [TestCase("""{"expiresAt":"later"}""", null)]
    [TestCase("""{"expiresAt":"{now}"}""", "expiresAt")]
    [TestCase("""{"expiresAt":"{nonUtc}"}""", "expiresAt")]
    [TestCase("""{"expiresAt":"{future}","maxUses":0}""", "maxUses")]
    [TestCase("""{"expiresAt":"{future}","maxUses":2147483648}""", null)]
    [TestCase("""{"expiresAt":"{future}","maxUses":1.5}""", null)]
    [TestCase("""{"expiresAt":"{future}","unexpected":true}""", null)]
    [TestCase("""{"expiresAt":"{future}","maxUses": """, null)]
    public async Task CreateInvite_WithInvalidBody_ShouldRejectWithoutPersisting(string template, string? field)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var now = Clock.UtcNow;
        var body = template
            .Replace("{now}", now.ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{future}", now.AddDays(1).ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{nonUtc}", now.AddDays(1).ToOffset(TimeSpan.FromHours(9)).ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        using var response = await SendCreateInviteAsync(space.PublicId, owner.AccessToken, body, NewIdempotencyKey());
        var error = await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        if (field is not null)
        {
            Assert.That(error.Error.Details.Any(detail => detail.Field == field && !string.IsNullOrEmpty(detail.Code)), Is.True,
                $"Validation must identify {field} with a field error code.");
        }

        Assert.That(await GetInvitesAsync(space.Id), Is.Empty);
    }

    [Test]
    public async Task CreateInvite_WithUnsupportedMediaType_ShouldRejectWithoutPersisting()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(1)), NewIdempotencyKey(), "text/plain");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.UnsupportedMediaType, "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetInvitesAsync(space.Id), Is.Empty);
    }

    [TestCase(null)]
    [TestCase(15)]
    [TestCase(129)]
    public async Task CreateInvite_WithMissingOrMalformedKey_ShouldRejectWithoutPersisting(int? length)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var key = length.HasValue ? new string('a', length.Value) : null;

        using var response = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(1)), key);
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetInvitesAsync(space.Id), Is.Empty);
    }

    [Test]
    public async Task CreateInvite_InArchivedSpace_ShouldRejectWithoutPersisting()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);

        using var response = await SendCreateInviteAsync(
            space.PublicId, owner.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(1)), NewIdempotencyKey());
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "SPACE_ARCHIVED");
        Assert.That(await GetInvitesAsync(space.Id), Is.Empty);
    }

    [Test]
    public Task CreateInvite_ShouldEnforceSessionAndInviteManagementPermission() =>
        AssertInviteManagementAuthorizationAsync((actor, space, _) => SendCreateInviteAsync(
            space.PublicId, actor?.AccessToken, ExpirationBody(Clock.UtcNow.AddDays(1)), NewIdempotencyKey()));

    private Task<HttpResponseMessage> SendCreateInviteAsync(
        Guid spacePublicId, string? token, string body, string? key, string mediaType = "application/json") =>
        Api.SendRawAsync(HttpMethod.Post, InviteListPath(spacePublicId), body, mediaType, token,
            configure: WithIdempotencyKey(key));

    private static string ExpirationBody(DateTimeOffset expiresAt) =>
        JsonSerializer.Serialize(new { expiresAt }, TestApiClient.JsonOptions);

    private static async Task<IssuedInvite> ReadCreatedInviteAsync(HttpResponseMessage response, Guid spacePublicId)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.Created);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        IntegrationAssert.AssertPropertyNames(root, "invitePublicId", "inviteToken", "inviteUrl", "expiresAt", "maxUses");
        var invitePublicId = root.GetProperty("invitePublicId").GetGuid();
        var token = root.GetProperty("inviteToken").GetString();
        var expiresAt = root.GetProperty("expiresAt").GetDateTimeOffset();
        var maxUses = root.GetProperty("maxUses");
        IntegrationAssert.AssertUuidV7(invitePublicId, "invitePublicId");
        Assert.That(invitePublicId.Variant, Is.InRange(8, 11));
        IntegrationAssert.AssertUtc(expiresAt, "expiresAt");
        AssertETag(response, "\"v1\"");
        Assert.That(
            token is not null && Regex.IsMatch(token, "\\Acs_inv_[A-Za-z0-9_-]{43}\\z"),
            Is.True,
            "The returned invite token must have the documented shape.");
        Assert.That(
            string.Equals(root.GetProperty("inviteUrl").GetString(), InvitePublicPath(token!), StringComparison.Ordinal),
            Is.True,
            "The invite URL must be a relative public lookup path.");
        Assert.That(
            string.Equals(TestApiClient.GetHeader(response, "Location"), InviteDetailPath(spacePublicId, invitePublicId),
                StringComparison.Ordinal),
            Is.True,
            "Location must address the managed invite by its public ID.");

        return new IssuedInvite(invitePublicId, token!, expiresAt, maxUses.ValueKind == JsonValueKind.Null ? null : maxUses.GetInt32());
    }

    private sealed class IssuedInvite(Guid publicId, string token, DateTimeOffset expiresAt, int? maxUses)
    {
        internal Guid PublicId { get; } = publicId;
        internal string Token { get; } = token;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal int? MaxUses { get; } = maxUses;
    }
}
