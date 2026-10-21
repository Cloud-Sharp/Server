using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Auth;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class AuthHttpContractTests : ApiIntegrationTestBase
{
    private static readonly Regex ServerCorrelationIdPattern =
        new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    [TestCase("/api/v2/auth/registrations", "{")]
    [TestCase(
        "/api/v2/auth/registrations",
        "{\"email\":\"user@example.com\",\"password\":\"{{password}}\",\"unknown\":true}")]
    [TestCase(
        "/api/v2/auth/registrations",
        "{\"email\":42,\"password\":\"{{password}}\"}")]
    [TestCase("/api/v2/auth/registrations", "null")]
    [TestCase("/api/v2/auth/sessions", "{")]
    [TestCase(
        "/api/v2/auth/sessions",
        "{\"loginId\":\"user@example.com\",\"password\":\"{{password}}\",\"unknown\":true}")]
    [TestCase("/api/v2/auth/sessions", "{\"loginId\":42,\"password\":\"{{password}}\"}")]
    public async Task BindingFailures_ShouldReturnStandardValidationError(
        string path,
        string json)
    {
        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            path,
            TestData.ExpandPasswordPlaceholders(json),
            "application/json");

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(error.Error.Details, Is.Empty);
    }

    [TestCase("{")]
    [TestCase(
        "{\"currentPassword\":\"{{password}}\",\"newPassword\":\"{{newPassword}}\",\"unknown\":true}")]
    [TestCase("{\"currentPassword\":42,\"newPassword\":\"{{newPassword}}\"}")]
    public async Task ChangePassword_WithMalformedBody_ShouldReturn400BeforeHandler(
        string json)
    {
        var registration = TestData.NewRegistration();
        var registered = await RegisterAsync(registration);
        var (_, etag) = await GetMeAsync(registered.AccessToken);

        using var response = await Api.SendRawAsync(
            HttpMethod.Put,
            "/api/v2/me/password",
            TestData.ExpandPasswordPlaceholders(json),
            "application/json",
            registered.AccessToken,
            etag);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(error.Error.Details, Is.Empty);

        var persisted = await FindUserAsync(registered.User.UserPublicId);
        Assert.That(
            Environment.PasswordHasher.Verify(
                persisted!.PasswordHash,
                registration.Password),
            Is.True);
    }

    [Test]
    public async Task UnsupportedMediaType_ShouldReturn415StandardError()
    {
        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            "{}",
            "text/plain");

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(error.Error.Details, Is.Empty);
    }

    [Test]
    public async Task CorrelationId_ShouldPropagateValidHeader()
    {
        const string correlationId = "corr-123_test";
        var registration = TestData.NewRegistration();
        await RegisterAsync(registration);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            registration,
            correlationId: correlationId);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.User.EmailConflict);
        Assert.Multiple(() =>
        {
            Assert.That(
                TestApiClient.GetHeader(
                    response,
                    TestApiClient.CorrelationHeaderName),
                Is.EqualTo(correlationId));
            Assert.That(error.RequestId, Is.EqualTo(correlationId));
        });
    }

    [TestCase("")]
    [TestCase("a b")]
    [TestCase("invalid!")]
    [TestCase("a/b")]
    public async Task CorrelationId_ShouldReplaceInvalidHeader(
        string correlationId)
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(TestData.NewEmail(), "wrong-password"),
            correlationId: correlationId);

        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
        var responseCorrelationId = TestApiClient.GetHeader(
            response,
            TestApiClient.CorrelationHeaderName);

        Assert.Multiple(() =>
        {
            Assert.That(responseCorrelationId, Is.Not.Null);
            Assert.That(
                ServerCorrelationIdPattern.IsMatch(responseCorrelationId!),
                Is.True,
                "Invalid correlation IDs must be replaced by a server-generated UUIDv7 value.");
            Assert.That(error.RequestId, Is.EqualTo(responseCorrelationId));
        });
    }

    [Test]
    public async Task CorrelationId_ShouldReplaceHeaderExceedingMaximumLength()
    {
        var correlationId = new string('a', 129);

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(TestData.NewEmail(), "wrong-password"),
            correlationId: correlationId);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
        var responseCorrelationId = TestApiClient.GetHeader(
            response,
            TestApiClient.CorrelationHeaderName);

        Assert.That(responseCorrelationId, Is.Not.Null);
        Assert.That(
            ServerCorrelationIdPattern.IsMatch(responseCorrelationId!),
            Is.True);
    }

    [Test]
    public async Task CorrelationId_ShouldReplaceDuplicateHeaders()
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(TestData.NewEmail(), "wrong-password"),
            configure: request =>
            {
                request.Headers.TryAddWithoutValidation(
                    TestApiClient.CorrelationHeaderName,
                    "first");
                request.Headers.TryAddWithoutValidation(
                    TestApiClient.CorrelationHeaderName,
                    "second");
            });

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Session.AuthInvalidCredentials);
        var responseCorrelationId = TestApiClient.GetHeader(
            response,
            TestApiClient.CorrelationHeaderName);

        Assert.That(responseCorrelationId, Is.Not.Null);
        Assert.That(
            ServerCorrelationIdPattern.IsMatch(responseCorrelationId!),
            Is.True);
    }

    [Test]
    public async Task SuccessResponses_ShouldUseCamelCaseAndStringEnums()
    {
        var registration = TestData.NewRegistration(
            userName: TestData.NewUserName(),
            displayName: TestData.DisplayName());

        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            registration);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        IntegrationAssert.AssertJsonContentType(response);
        IntegrationAssert.AssertNoStore(response);

        var json = await TestApiClient.ReadBodyAsync(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        IntegrationAssert.AssertPropertyNames(
            root,
            "accessToken",
            "tokenType",
            "idleExpiresAt",
            "absoluteExpiresAt",
            "user");

        var user = root.GetProperty("user");
        IntegrationAssert.AssertOnlyPublicUserProperties(user);
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("tokenType").GetString(), Is.EqualTo("Bearer"));
            Assert.That(root.GetProperty("idleExpiresAt").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(root.GetProperty("absoluteExpiresAt").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(user.GetProperty("systemRole").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(user.GetProperty("status").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(user.GetProperty("systemRole").GetString(), Is.EqualTo("USER"));
            Assert.That(user.GetProperty("status").GetString(), Is.EqualTo("ACTIVE"));
            Assert.That(
                Guid.TryParse(user.GetProperty("userPublicId").GetString(), out var publicId),
                Is.True);
            Assert.That(publicId, Is.Not.EqualTo(Guid.Empty));
        });
    }
}
