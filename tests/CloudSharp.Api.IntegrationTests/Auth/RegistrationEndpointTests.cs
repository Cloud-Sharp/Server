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
public sealed class RegistrationEndpointTests : ApiIntegrationTestBase
{
    [Test]
    public async Task Register_ShouldCreateAccountAndSession()
    {
        var request = TestData.NewRegistration(
            userName: TestData.NewUserName(),
            displayName: "  Cloud User  ");
        var issuedAt = Clock.UtcNow;

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/api/v2/me"));
        });
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
            Assert.That(body.AccessToken.Length, Is.GreaterThan("cs_sess_".Length));
            Assert.That(body.IdleExpiresAt, Is.EqualTo(issuedAt.AddHours(24)));
            Assert.That(body.AbsoluteExpiresAt, Is.EqualTo(issuedAt.AddDays(7)));
            Assert.That(body.User.Email, Is.EqualTo(request.Email));
            Assert.That(body.User.UserName, Is.EqualTo(request.UserName));
            Assert.That(body.User.DisplayName, Is.EqualTo("Cloud User"));
            Assert.That(body.User.SystemRole, Is.EqualTo("USER"));
            Assert.That(body.User.Status, Is.EqualTo("ACTIVE"));
            IntegrationAssert.AssertUtc(body.User.CreatedAt, nameof(body.User.CreatedAt));
            IntegrationAssert.AssertUtc(body.User.UpdatedAt, nameof(body.User.UpdatedAt));
            IntegrationAssert.AssertUuidV7(body.User.UserPublicId, nameof(body.User.UserPublicId));
        });

        var json = await TestApiClient.ReadBodyAsync(response);
        IntegrationAssert.AssertNoInternalIdentifiers(json);
        IntegrationAssert.AssertDoesNotContain(
            json,
            request.Password,
            "The registration response must not contain the password.");

        using (var document = JsonDocument.Parse(json))
        {
            IntegrationAssert.AssertOnlyPublicUserProperties(
                document.RootElement.GetProperty("user"));
        }

        var persisted = await FindUserAsync(body.User.UserPublicId);
        Assert.That(persisted, Is.Not.Null, "The registered user must be persisted.");
        var user = persisted!;

        Assert.Multiple(() =>
        {
            Assert.That(user.Email, Is.EqualTo(request.Email));
            Assert.That(user.NormalizedEmail, Is.EqualTo(request.Email.ToUpperInvariant()));
            Assert.That(user.UserName, Is.EqualTo(request.UserName));
            Assert.That(
                user.NormalizedUserName,
                Is.EqualTo(request.UserName!.ToUpperInvariant()));
            Assert.That(user.DisplayName, Is.EqualTo("Cloud User"));
            Assert.That(
                Environment.PasswordHasher.Verify(user.PasswordHash, request.Password),
                Is.True,
                "The stored password hash must verify the original password.");
            Assert.That(
                string.Equals(user.PasswordHash, request.Password, StringComparison.Ordinal),
                Is.False,
                "The stored password hash must not be the plain password.");
            Assert.That(user.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.SecurityVersion, Is.EqualTo(1));
            Assert.That(user.Version, Is.EqualTo(1));
            Assert.That(user.CreatedAt, Is.EqualTo(issuedAt));
            Assert.That(user.UpdatedAt, Is.EqualTo(issuedAt));
            Assert.That(user.SuspendedAt, Is.Null);
            Assert.That(user.DeletedAt, Is.Null);
        });
        IntegrationAssert.AssertDoesNotContain(
            json,
            user.PasswordHash,
            "The registration response must not contain the password hash.");

        var sessionExists = await SessionBodyExistsAsync(body.AccessToken);
        var sessionUserId = await GetSessionFieldAsync(body.AccessToken, "userId");
        var sessionUserPublicId = await GetSessionFieldAsync(body.AccessToken, "userPublicId");
        var sessionSecurityVersion = await GetSessionFieldAsync(
            body.AccessToken,
            "securityVersion");
        Assert.Multiple(() =>
        {
            Assert.That(sessionExists, Is.True);
            Assert.That(
                sessionUserId,
                Is.EqualTo(user.Id.ToString(CultureInfo.InvariantCulture)));
            Assert.That(
                sessionUserPublicId,
                Is.EqualTo(user.PublicId.ToString("N")));
            Assert.That(sessionSecurityVersion, Is.EqualTo("1"));
        });

        var indexMembers = await GetSessionIndexMembersAsync(user.Id);
        var tokenHash = Environment.TokenHasher.Hash(body.AccessToken);
        Assert.Multiple(() =>
        {
            Assert.That(indexMembers.Count, Is.EqualTo(1));
            Assert.That(
                indexMembers.Contains(tokenHash, StringComparer.Ordinal),
                Is.True,
                "The session index must contain the issued token hash.");
        });
        IntegrationAssert.AssertDoesNotContain(
            json,
            tokenHash,
            "The registration response must not contain the session token hash.");

        var (profile, etag) = await GetMeAsync(body.AccessToken);
        Assert.Multiple(() =>
        {
            Assert.That(etag, Is.EqualTo("\"v1\""));
            Assert.That(profile.UserPublicId, Is.EqualTo(body.User.UserPublicId));
            Assert.That(profile.Email, Is.EqualTo(request.Email));
        });
    }

    [Test]
    public async Task Register_WithDuplicateEmail_ShouldReturn409WithoutNewUserOrSession()
    {
        var first = await RegisterAsync(
            TestData.NewRegistration(userName: TestData.NewUserName()));
        var userCount = await CountUsersAsync();
        var userId = await GetUserIdAsync(first.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            TestData.NewRegistration(
                email: first.User.Email.ToUpperInvariant(),
                userName: TestData.NewUserName()));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.User.EmailConflict);

        var currentUserCount = await CountUsersAsync();
        var currentSessionCount = await GetSessionIndexCountAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(currentUserCount, Is.EqualTo(userCount));
            Assert.That(currentSessionCount, Is.EqualTo(sessionCount));
        });
    }

    [Test]
    public async Task Register_WithDuplicateUserName_ShouldReturn409WithoutNewUserOrSession()
    {
        var userName = TestData.NewUserName();
        var first = await RegisterAsync(TestData.NewRegistration(userName: userName));
        var userCount = await CountUsersAsync();
        var userId = await GetUserIdAsync(first.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            TestData.NewRegistration(userName: userName.ToUpperInvariant()));

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.User.NameConflict);

        var currentUserCount = await CountUsersAsync();
        var currentSessionCount = await GetSessionIndexCountAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(currentUserCount, Is.EqualTo(userCount));
            Assert.That(currentSessionCount, Is.EqualTo(sessionCount));
        });
    }

    [Test]
    public async Task Register_WithConcurrentDuplicateEmail_ShouldCreateSingleAccount()
    {
        var email = TestData.NewEmail();
        var firstRequest = TestData.NewRegistration(
            email: email,
            userName: TestData.NewUserName());
        var secondRequest = TestData.NewRegistration(
            email: email.ToUpperInvariant(),
            userName: TestData.NewUserName());

        var responses = await Task.WhenAll(
            Api.SendJsonAsync(HttpMethod.Post, "/api/v2/auth/registrations", firstRequest),
            Api.SendJsonAsync(HttpMethod.Post, "/api/v2/auth/registrations", secondRequest));

        using var first = responses[0];
        using var second = responses[1];

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(1));
            Assert.That(conflicts, Is.EqualTo(1));
        });

        var conflictResponse = first.StatusCode == HttpStatusCode.Conflict ? first : second;
        await IntegrationAssert.AssertErrorAsync(
            conflictResponse,
            HttpStatusCode.Conflict,
            ErrorCodes.User.EmailConflict);

        var createdResponse = first.StatusCode == HttpStatusCode.Created ? first : second;
        var body = await TestApiClient.ReadJsonAsync<AuthSessionWireResponse>(createdResponse);
        var userId = await GetUserIdAsync(body.User.UserPublicId);

        var userCount = await CountUsersAsync();
        var sessionCount = await GetSessionIndexCountAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(userCount, Is.EqualTo(1));
            Assert.That(sessionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Register_WithOptionalProfileFieldsOmitted_ShouldPersistNulls()
    {
        var request = TestData.NewRegistration();
        var body = await RegisterAsync(request);

        var persisted = await FindUserAsync(body.User.UserPublicId);
        Assert.That(persisted, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(body.User.UserName, Is.Null);
            Assert.That(body.User.DisplayName, Is.Null);
            Assert.That(persisted!.UserName, Is.Null);
            Assert.That(persisted.NormalizedUserName, Is.Null);
            Assert.That(persisted.DisplayName, Is.Null);
        });
    }

    [TestCase(
        "{\"email\":\"not-an-email\",\"password\":\"{{password}}\"}",
        "email")]
    [TestCase(
        "{\"email\":\"user@example.com\",\"password\":\"short\"}",
        "password")]
    [TestCase(
        "{\"password\":\"{{password}}\"}",
        "email")]
    [TestCase(
        "{\"email\":\"user@example.com\"}",
        "password")]
    public async Task Register_WithInvalidRequestFields_ShouldReturn400ValidationError(
        string json,
        string field)
    {
        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            TestData.ExpandPasswordPlaceholders(json),
            "application/json");

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");

        Assert.That(
            error.Error.Details.Any(detail => detail.Field == field && detail.Code == "INVALID"),
            Is.True,
            $"The validation response must contain an {field} INVALID detail.");
    }
}
