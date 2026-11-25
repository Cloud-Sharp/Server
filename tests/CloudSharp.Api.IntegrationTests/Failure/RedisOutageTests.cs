using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Failure;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class RedisOutageTests
{
    private ApiIntegrationEnvironment _environment = null!;
    private TestApiClient _api = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _environment = await ApiIntegrationEnvironment.StartAsync(
            new ApiIntegrationEnvironmentOptions { BindRedisPort = true });
        _api = _environment.CreateClient();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        _api?.Dispose();
        if (_environment is not null)
        {
            await _environment.DisposeAsync();
        }
    }

    [Test]
    public async Task UnavailableRedis_ShouldFailClosedAndPreserveCommittedRegistration()
    {
        var firstRegistration = TestData.NewRegistration();
        var first = await TestFlows.RegisterAsync(_api, firstRegistration);
        await TestFlows.GetMeAsync(_api, first.AccessToken);

        await _environment.StopRedisAsync(save: false);
        await _environment.WaitForAppRedisConnectionAsync(
            false,
            TimeSpan.FromSeconds(20));

        using var protectedResponse = await _api.GetAsync(
            "/api/v2/me",
            first.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            protectedResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
        var protectedBody = await TestApiClient.ReadBodyAsync(protectedResponse);
        IntegrationAssert.AssertDoesNotContain(
            protectedBody,
            first.AccessToken,
            "The dependency error must not contain the session token.");

        using var loginResponse = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(firstRegistration.Email, firstRegistration.Password));
        await IntegrationAssert.AssertErrorAsync(
            loginResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);

        var secondRegistration = TestData.NewRegistration();
        var key = Guid.NewGuid().ToString("N");
        void UseOriginalKey(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        using var registrationResponse = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            secondRegistration,
            configure: UseOriginalKey);
        await IntegrationAssert.AssertErrorAsync(
            registrationResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
        var registrationBody = await TestApiClient.ReadBodyAsync(registrationResponse);
        IntegrationAssert.AssertDoesNotContain(
            registrationBody,
            secondRegistration.Password,
            "The dependency error must not contain the password.");
        IntegrationAssert.AssertDoesNotContain(
            registrationBody,
            "accessToken",
            "A failed registration must not return an access token.");

        using var committedRetry = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            secondRegistration,
            configure: UseOriginalKey);
        await IntegrationAssert.AssertErrorAsync(
            committedRetry, HttpStatusCode.Conflict, "SECRET_RESPONSE_NOT_REPLAYABLE");
        using var retryBody = JsonDocument.Parse(await TestApiClient.ReadBodyAsync(committedRetry));
        var committedPublicId = retryBody.RootElement.GetProperty("userPublicId").GetGuid();

        await _environment.StartRedisAsync();

        var second = await TestFlows.LoginAsync(
            _api,
            new LoginWireRequest(secondRegistration.Email, secondRegistration.Password));
        var (secondProfile, _) = await TestFlows.GetMeAsync(_api, second.AccessToken);
        Assert.That(secondProfile.Email, Is.EqualTo(secondRegistration.Email));
        Assert.That(secondProfile.UserPublicId, Is.EqualTo(committedPublicId));

        var persisted = await _environment.FindUserAsync(secondProfile.UserPublicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.That(
            _environment.PasswordHasher.Verify(
                persisted!.PasswordHash,
                secondRegistration.Password),
            Is.True,
            "The committed registration must remain usable after Redis recovers.");

        using var duplicate = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            secondRegistration);
        await IntegrationAssert.AssertErrorAsync(
            duplicate,
            HttpStatusCode.Conflict,
            ErrorCodes.User.EmailConflict);

        Assert.That(await _environment.CountUsersAsync(), Is.EqualTo(2));
    }
}
