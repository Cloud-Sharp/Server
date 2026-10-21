using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Auth;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class LoginEndpointTests : ApiIntegrationTestBase
{
    [Test]
    public async Task Login_ShouldIssueSessionForNormalizedEmail()
    {
        var registration = TestData.NewRegistration(
            email: $"user-{Guid.NewGuid():N}@Example.com",
            userName: TestData.NewUserName());
        var registered = await RegisterAsync(registration);
        var issuedAt = Clock.UtcNow;

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(
                $"  {registration.Email.ToLowerInvariant()}  ",
                registration.Password));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        IntegrationAssert.AssertJsonContentType(response);
        IntegrationAssert.AssertNoStore(response);

        var body = await TestApiClient.ReadJsonAsync<AuthSessionWireResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.TokenType, Is.EqualTo("Bearer"));
            Assert.That(
                body.AccessToken.StartsWith("cs_sess_", StringComparison.Ordinal),
                Is.True,
                "The issued access token must use the session prefix.");
            Assert.That(body.IdleExpiresAt, Is.EqualTo(issuedAt.AddHours(24)));
            Assert.That(body.AbsoluteExpiresAt, Is.EqualTo(issuedAt.AddDays(7)));
            Assert.That(body.User.UserPublicId, Is.EqualTo(registered.User.UserPublicId));
            Assert.That(body.User.Email, Is.EqualTo(registration.Email));
            Assert.That(body.User.UserName, Is.EqualTo(registration.UserName));
            Assert.That(body.User.SystemRole, Is.EqualTo("USER"));
            Assert.That(body.User.Status, Is.EqualTo("ACTIVE"));
        });

        var json = await TestApiClient.ReadBodyAsync(response);
        IntegrationAssert.AssertNoInternalIdentifiers(json);
        IntegrationAssert.AssertDoesNotContain(
            json,
            registration.Password,
            "The login response must not contain the password.");
        Assert.That(
            string.Equals(body.AccessToken, registered.AccessToken, StringComparison.Ordinal),
            Is.False,
            "Each login must issue an independent session token.");

        var userId = await GetUserIdAsync(body.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);
        var newSessionExists = await SessionBodyExistsAsync(body.AccessToken);
        var originalSessionExists = await SessionBodyExistsAsync(registered.AccessToken);
        Assert.Multiple(() =>
        {
            Assert.That(sessionCount, Is.EqualTo(2));
            Assert.That(newSessionExists, Is.True);
            Assert.That(originalSessionExists, Is.True);
        });
    }

    [Test]
    public async Task Login_WithUnknownAccount_ShouldReturn401InvalidCredentials()
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(TestData.NewEmail(), TestData.Password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);

        var body = await TestApiClient.ReadBodyAsync(response);
        IntegrationAssert.AssertDoesNotContain(
            body,
            TestData.Password,
            "The login error response must not contain the password.");
    }

    [Test]
    public async Task Login_WithWrongPassword_ShouldReturn401WithoutIssuingSession()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var userId = await GetUserIdAsync(registered.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(registration.Email, "wrong-password"));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);

        Assert.That(await GetSessionIndexCountAsync(userId), Is.EqualTo(sessionCount));
    }

    [Test]
    public async Task Login_WithSuspendedUser_ShouldReturn401WithoutIssuingSession()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        await SuspendUserAsync(registered.User.UserPublicId);
        var userId = await GetUserIdAsync(registered.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(registration.Email, registration.Password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);

        Assert.That(await GetSessionIndexCountAsync(userId), Is.EqualTo(sessionCount));
    }

    [Test]
    public async Task Login_WithDeletedUser_ShouldReturn401WithoutIssuingSession()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        await DeleteUserAsync(registered.User.UserPublicId);
        var userId = await GetUserIdAsync(registered.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(registration.Email, registration.Password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);

        Assert.That(await GetSessionIndexCountAsync(userId), Is.EqualTo(sessionCount));
    }

    [TestCase("")]
    [TestCase("not-an-email")]
    [TestCase("user name@example.com")]
    public async Task Login_WithInvalidLoginId_ShouldReturn401InvalidCredentials(string loginId)
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(loginId, TestData.Password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Login_WithInvalidPasswordShape_ShouldReturn401InvalidCredentials(string password)
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(TestData.NewEmail(), password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
    }

    [Test]
    public async Task Login_WithUserNameInsteadOfEmail_ShouldReturn401InvalidCredentials()
    {
        var registration = TestData.NewRegistration(userName: TestData.NewUserName());
        await RegisterAsync(registration);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(registration.UserName!, registration.Password));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
    }

    [Test]
    public async Task Login_WithEmptyBody_ShouldReturn401InvalidCredentials()
    {
        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            "{}",
            "application/json");

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
    }
}
