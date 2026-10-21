using System.Globalization;
using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Auth;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class SessionAuthenticationEndpointTests : ApiIntegrationTestBase
{
    [Test]
    public async Task GetMe_ShouldReturnProfileAndVersionETag()
    {
        var registration = TestData.NewRegistration(
            userName: TestData.NewUserName(),
            displayName: TestData.DisplayName());
        var registered = await RegisterAsync(registration);
        var other = await RegisterAsync(
            TestData.NewRegistration(userName: TestData.NewUserName()));

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        IntegrationAssert.AssertJsonContentType(response);
        IntegrationAssert.AssertNoStore(response);
        Assert.That(
            TestApiClient.GetHeader(response, "ETag"),
            Is.EqualTo("\"v1\""));

        var body = await TestApiClient.ReadJsonAsync<UserWireResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.UserPublicId, Is.EqualTo(registered.User.UserPublicId));
            Assert.That(body.Email, Is.EqualTo(registration.Email));
            Assert.That(body.UserName, Is.EqualTo(registration.UserName));
            Assert.That(body.DisplayName, Is.EqualTo(registration.DisplayName));
            Assert.That(body.SystemRole, Is.EqualTo("USER"));
            Assert.That(body.Status, Is.EqualTo("ACTIVE"));
            IntegrationAssert.AssertUtc(body.CreatedAt, nameof(body.CreatedAt));
            IntegrationAssert.AssertUtc(body.UpdatedAt, nameof(body.UpdatedAt));
            IntegrationAssert.AssertUuidV7(body.UserPublicId, nameof(body.UserPublicId));
        });

        var json = await TestApiClient.ReadBodyAsync(response);
        IntegrationAssert.AssertNoInternalIdentifiers(json);
        IntegrationAssert.AssertDoesNotContain(
            json,
            other.User.Email,
            "The profile response must not expose another user's email.");
        IntegrationAssert.AssertDoesNotContain(
            json,
            other.User.UserPublicId.ToString("D"),
            "The profile response must not expose another user's public id.");
        IntegrationAssert.AssertDoesNotContain(
            json,
            registration.Password,
            "The profile response must not contain the password.");

        using var document = JsonDocument.Parse(json);
        IntegrationAssert.AssertOnlyPublicUserProperties(document.RootElement);
    }

    [Test]
    public async Task GetMe_WithoutToken_ShouldReturn401TokenRequired()
    {
        using var response = await Api.GetAsync("/api/v2/me");

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenRequired);
        Assert.That(
            TestApiClient.GetHeader(response, "WWW-Authenticate"),
            Is.EqualTo("Bearer"));
    }

    [TestCaseSource(nameof(MalformedAuthorizationCases))]
    public async Task GetMe_WithMalformedAuthorization_ShouldReturn401(
        string authorization,
        string expectedCode)
    {
        using var response = await Api.GetAsync(
            "/api/v2/me",
            configure: request => request.Headers.TryAddWithoutValidation(
                "Authorization",
                authorization));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            expectedCode);
        Assert.That(
            TestApiClient.GetHeader(response, "WWW-Authenticate"),
            Is.EqualTo("Bearer"));
    }

    [Test]
    public async Task GetMe_WithDuplicateAuthorizationHeaders_ShouldReturn401TokenInvalid()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());

        using var response = await Api.GetAsync(
            "/api/v2/me",
            configure: request =>
            {
                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    $"Bearer {registered.AccessToken}");
                request.Headers.TryAddWithoutValidation(
                    "Authorization",
                    $"Bearer {registered.AccessToken}");
            });

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task GetMe_WithMissingSessionInRedis_ShouldReturn401TokenInvalid()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        await DeleteSessionAsync(registered.AccessToken);

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [TestCase("missing-security-version")]
    [TestCase("empty-session-id")]
    [TestCase("zero-security-version")]
    [TestCase("reversed-expiry-order")]
    public async Task GetMe_WithCorruptSessionPayload_ShouldReturn401TokenInvalid(
        string corruption)
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        var reversedIdleExpiresAt = Clock.UtcNow
            .AddMinutes(-1)
            .UtcTicks
            .ToString("D19", CultureInfo.InvariantCulture);

        switch (corruption)
        {
            case "missing-security-version":
                await DeleteSessionFieldAsync(registered.AccessToken, "securityVersion");
                break;
            case "empty-session-id":
                await SetSessionFieldsAsync(
                    registered.AccessToken,
                    ("sessionId", Guid.Empty.ToString("N")));
                break;
            case "zero-security-version":
                await SetSessionFieldsAsync(
                    registered.AccessToken,
                    ("securityVersion", "0"));
                break;
            case "reversed-expiry-order":
                await SetSessionFieldsAsync(
                    registered.AccessToken,
                    ("idleExpiresAt", reversedIdleExpiresAt));
                break;
            default:
                Assert.Fail("Unknown corruption case.");
                break;
        }

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
        Assert.That(
            await SessionBodyExistsAsync(registered.AccessToken),
            Is.False,
            "A corrupt session body must not remain usable.");
    }

    [Test]
    public async Task GetMe_WithIdleExpiredSession_ShouldReturn401AtBoundary()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        var idleExpiresAt = registered.IdleExpiresAt;

        Clock.Set(idleExpiresAt.AddSeconds(-1));
        var (profile, _) = await GetMeAsync(registered.AccessToken);
        Assert.That(profile.UserPublicId, Is.EqualTo(registered.User.UserPublicId));

        Clock.Set(idleExpiresAt);
        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task GetMe_WithAbsoluteExpiredSession_ShouldReturn401AtBoundary()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        var absoluteExpiresAt = registered.AbsoluteExpiresAt;
        await SetSessionFieldsAsync(
            registered.AccessToken,
            ("idleExpiresAt", absoluteExpiresAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)));

        Clock.Set(absoluteExpiresAt.AddSeconds(-1));
        var (profile, _) = await GetMeAsync(registered.AccessToken);
        Assert.That(profile.UserPublicId, Is.EqualTo(registered.User.UserPublicId));

        Clock.Set(absoluteExpiresAt);
        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task Login_WithMoreThanTenSessions_ShouldEvictOldestSession()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var tokens = new List<string> { registered.AccessToken };

        for (var index = 0; index < 10; index++)
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            var login = await LoginAsync(
                new LoginWireRequest(registration.Email, registration.Password));
            tokens.Add(login.AccessToken);
        }

        var userId = await GetUserIdAsync(registered.User.UserPublicId);
        var members = await GetSessionIndexMembersAsync(userId);
        var oldestSessionExists = await SessionBodyExistsAsync(tokens[0]);
        Assert.Multiple(() =>
        {
            Assert.That(members.Count, Is.EqualTo(10));
            Assert.That(oldestSessionExists, Is.False);
        });

        using (var evicted = await Api.GetAsync("/api/v2/me", tokens[0]))
        {
            await IntegrationAssert.AssertErrorAsync(
                evicted,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Auth.TokenInvalid);
        }

        foreach (var token in tokens.Skip(1))
        {
            var (profile, _) = await GetMeAsync(token);
            Assert.That(
                profile.UserPublicId,
                Is.EqualTo(registered.User.UserPublicId));
        }
    }

    [Test]
    public async Task GetMe_ShouldReflectLatestSystemRoleWithoutReissuingToken()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        await ChangeSystemRoleAsync(
            registered.User.UserPublicId,
            SystemRole.SystemAdmin);

        var (profile, etag) = await GetMeAsync(registered.AccessToken);

        Assert.Multiple(() =>
        {
            Assert.That(profile.SystemRole, Is.EqualTo("SYSTEM_ADMIN"));
            Assert.That(etag, Is.EqualTo("\"v2\""));
        });
    }

    [Test]
    public async Task GetMe_WithChangedSecurityVersion_ShouldRejectSession()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        await BumpSecurityVersionAsync(registered.User.UserPublicId);

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task GetMe_WithSuspendedUser_ShouldRejectSession()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        await SuspendUserAsync(registered.User.UserPublicId);

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task GetMe_WithDeletedUser_ShouldRejectSession()
    {
        var registered = await RegisterAsync(TestData.NewRegistration());
        await DeleteUserAsync(registered.User.UserPublicId);

        using var response = await Api.GetAsync("/api/v2/me", registered.AccessToken);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenInvalid);
    }

    private static IEnumerable<TestCaseData> MalformedAuthorizationCases()
    {
        yield return new TestCaseData("Basic abc", ErrorCodes.Auth.TokenInvalid)
            .SetName("GetMe_WithMalformedAuthorization_ShouldReturn401(NonBearerScheme)");
        yield return new TestCaseData("Bearer cs_other_forged", ErrorCodes.Auth.TokenInvalid)
            .SetName("GetMe_WithMalformedAuthorization_ShouldReturn401(UnknownPrefix)");
        yield return new TestCaseData("Bearer cs_mcp_forged", ErrorCodes.Auth.TokenTypeNotAllowed)
            .SetName("GetMe_WithMalformedAuthorization_ShouldReturn401(McpPrefix)");
        yield return new TestCaseData("Bearer cs_sess_forged", ErrorCodes.Auth.TokenInvalid)
            .SetName("GetMe_WithMalformedAuthorization_ShouldReturn401(ForgedSessionToken)");
        yield return new TestCaseData(
                $"Bearer cs_sess_{new string('a', 600)}",
                ErrorCodes.Auth.TokenInvalid)
            .SetName("GetMe_WithMalformedAuthorization_ShouldReturn401(OversizedToken)");
    }
}
