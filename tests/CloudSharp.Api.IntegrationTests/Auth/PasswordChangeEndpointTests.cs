using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Auth;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class PasswordChangeEndpointTests : ApiIntegrationTestBase
{
    [Test]
    public async Task ChangePassword_ShouldKeepCurrentSessionAndRevokeOtherSessions()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var other = await LoginAsync(
            new LoginWireRequest(registration.Email, registration.Password));
        var (_, etag) = await GetMeAsync(registered.AccessToken);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            registration.Password,
            TestData.NewPassword,
            etag);

        var body = await TestApiClient.ReadBodyAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(body, Is.Empty);
            Assert.That(TestApiClient.GetHeader(response, "ETag"), Is.EqualTo("\"v2\""));
        });
        IntegrationAssert.AssertNoStore(response);

        var (profile, updatedEtag) = await GetMeAsync(registered.AccessToken);
        Assert.Multiple(() =>
        {
            Assert.That(profile.UserPublicId, Is.EqualTo(registered.User.UserPublicId));
            Assert.That(updatedEtag, Is.EqualTo("\"v2\""));
        });

        using (var revoked = await Api.GetAsync("/api/v2/me", other.AccessToken))
        {
            await IntegrationAssert.AssertErrorAsync(
                revoked,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Auth.TokenInvalid);
        }

        using (var oldPassword = await Api.SendJsonAsync(
                   HttpMethod.Post,
                   "/api/v2/auth/sessions",
                   new LoginWireRequest(registration.Email, registration.Password)))
        {
            await IntegrationAssert.AssertErrorAsync(
                oldPassword,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Session.AuthInvalidCredentials);
        }

        var newLogin = await LoginAsync(
            new LoginWireRequest(registration.Email, TestData.NewPassword));
        var (newProfile, _) = await GetMeAsync(newLogin.AccessToken);
        Assert.That(
            newProfile.UserPublicId,
            Is.EqualTo(registered.User.UserPublicId));

        var after = await FindUserAsync(registered.User.UserPublicId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.Version, Is.EqualTo(2));
            Assert.That(after.SecurityVersion, Is.EqualTo(2));
            Assert.That(after.UpdatedAt, Is.EqualTo(Clock.UtcNow));
            Assert.That(
                Environment.PasswordHasher.Verify(after.PasswordHash, TestData.NewPassword),
                Is.True,
                "The new password must verify against the persisted hash.");
            Assert.That(
                Environment.PasswordHasher.Verify(after.PasswordHash, registration.Password),
                Is.False,
                "The old password must not verify against the persisted hash.");
            Assert.That(
                string.Equals(after.PasswordHash, before!.PasswordHash, StringComparison.Ordinal),
                Is.False,
                "The password hash must change.");
        });

        var currentExists = await SessionBodyExistsAsync(registered.AccessToken);
        var otherExists = await SessionBodyExistsAsync(other.AccessToken);
        var currentSecurityVersion = await GetSessionFieldAsync(
            registered.AccessToken,
            "securityVersion");
        var userId = await GetUserIdAsync(registered.User.UserPublicId);
        var sessionCount = await GetSessionIndexCountAsync(userId);
        Assert.Multiple(() =>
        {
            Assert.That(currentExists, Is.True);
            Assert.That(otherExists, Is.False);
            Assert.That(currentSecurityVersion, Is.EqualTo("2"));
            Assert.That(sessionCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ChangePassword_WithoutIfMatch_ShouldReturn428WithoutChangingState()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            registration.Password,
            TestData.NewPassword,
            ifMatch: null);

        await IntegrationAssert.AssertErrorAsync(
            response,
            (HttpStatusCode)428,
            ErrorCodes.Common.PreconditionRequired);

        await AssertPasswordUnchangedAsync(before!, registration.Password);
    }

    [TestCaseSource(nameof(InvalidIfMatchValues))]
    public async Task ChangePassword_WithInvalidIfMatch_ShouldReturn400(
        string ifMatch)
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        await GetMeAsync(registered.AccessToken);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            registration.Password,
            TestData.NewPassword,
            ifMatch);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(
            error.Error.Details.Any(detail =>
                detail.Field == "ifMatch" && detail.Code == "INVALID_ETAG"),
            Is.True);

        await AssertPasswordUnchangedAsync(before!, registration.Password);
    }

    [Test]
    public async Task ChangePassword_WithMultipleIfMatchHeaders_ShouldReturn400()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        await GetMeAsync(registered.AccessToken);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Put,
            "/api/v2/me/password",
            new ChangePasswordWireRequest(
                registration.Password,
                TestData.NewPassword),
            registered.AccessToken,
            configure: request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
                request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
            });

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");

        await AssertPasswordUnchangedAsync(before!, registration.Password);
    }

    [Test]
    public async Task ChangePassword_WithStaleVersion_ShouldReturn412WithoutChangingState()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var (_, etag) = await GetMeAsync(registered.AccessToken);
        await BumpVersionAsync(registered.User.UserPublicId);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            registration.Password,
            TestData.NewPassword,
            etag);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.PreconditionFailed,
            ErrorCodes.Common.PreconditionFailed);

        var after = await FindUserAsync(registered.User.UserPublicId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.Version, Is.EqualTo(before!.Version));
            Assert.That(after.SecurityVersion, Is.EqualTo(before.SecurityVersion));
            Assert.That(
                Environment.PasswordHasher.Verify(after.PasswordHash, registration.Password),
                Is.True);
            Assert.That(
                Environment.PasswordHasher.Verify(after.PasswordHash, TestData.NewPassword),
                Is.False);
        });

        var (profile, _) = await GetMeAsync(registered.AccessToken);
        Assert.That(
            profile.UserPublicId,
            Is.EqualTo(registered.User.UserPublicId));
    }

    [Test]
    public async Task ChangePassword_WithWrongCurrentPassword_ShouldReturn400WithoutChangingState()
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var (_, etag) = await GetMeAsync(registered.AccessToken);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            "wrong-password",
            TestData.NewPassword,
            etag);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.User.PasswordMismatch);
        var body = await TestApiClient.ReadBodyAsync(response);
        Assert.Multiple(() =>
        {
            IntegrationAssert.AssertDoesNotContain(
                body,
                registration.Password,
                "The error response must not contain the current password.");
            IntegrationAssert.AssertDoesNotContain(
                body,
                TestData.NewPassword,
                "The error response must not contain the new password.");
        });

        await AssertPasswordUnchangedAsync(before!, registration.Password);
    }

    [TestCase("short", "newPassword")]
    [TestCase("", "newPassword")]
    public async Task ChangePassword_WithInvalidNewPassword_ShouldReturn400ValidationError(
        string newPassword,
        string field)
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var (_, etag) = await GetMeAsync(registered.AccessToken);
        var before = await FindUserAsync(registered.User.UserPublicId);

        using var response = await ChangePasswordAsync(
            registered.AccessToken,
            registration.Password,
            newPassword,
            etag);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(
            error.Error.Details.Any(detail =>
                detail.Field == field && detail.Code == "INVALID"),
            Is.True);

        await AssertPasswordUnchangedAsync(before!, registration.Password);
    }

    private static IEnumerable<TestCaseData> InvalidIfMatchValues()
    {
        yield return new TestCaseData("*")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(Wildcard)");
        yield return new TestCaseData("v1")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(Unquoted)");
        yield return new TestCaseData("\"v0\"")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(NonPositiveVersion)");
        yield return new TestCaseData("W/\"v1\"")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(WeakETag)");
        yield return new TestCaseData("\"v1\", \"v2\"")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(MultipleETags)");
        yield return new TestCaseData("\"x1\"")
            .SetName("ChangePassword_WithInvalidIfMatch_ShouldReturn400(WrongPrefix)");
    }

    private async Task AssertPasswordUnchangedAsync(
        UserSnapshot before,
        string currentPassword)
    {
        var after = await FindUserAsync(before.PublicId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                string.Equals(after!.PasswordHash, before.PasswordHash, StringComparison.Ordinal),
                Is.True,
                "The password hash must not change.");
            Assert.That(after.SecurityVersion, Is.EqualTo(before.SecurityVersion));
            Assert.That(after.Version, Is.EqualTo(before.Version));
            Assert.That(
                Environment.PasswordHasher.Verify(after.PasswordHash, currentPassword),
                Is.True);
            Assert.That(
                Environment.PasswordHasher.Verify(
                    after.PasswordHash,
                    TestData.NewPassword),
                Is.False);
        });
    }
}
