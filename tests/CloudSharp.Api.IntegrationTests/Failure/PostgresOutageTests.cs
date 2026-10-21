using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Failure;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class PostgresOutageTests
{
    private ApiIntegrationEnvironment _environment = null!;
    private TestApiClient _api = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _environment = await ApiIntegrationEnvironment.StartAsync();
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
    public async Task UnavailablePostgres_ShouldReturn503ForDatabaseDependentRequests()
    {
        var registration = TestData.NewRegistration();
        var registered = await TestFlows.RegisterAsync(_api, registration);
        await TestFlows.GetMeAsync(_api, registered.AccessToken);

        await _environment.StopPostgresAsync();

        using var protectedResponse = await _api.GetAsync(
            "/api/v2/me",
            registered.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            protectedResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
        var protectedBody = await TestApiClient.ReadBodyAsync(protectedResponse);
        IntegrationAssert.AssertDoesNotContain(
            protectedBody,
            registered.AccessToken,
            "The dependency error must not contain the session token.");

        using var loginResponse = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            new LoginWireRequest(registration.Email, registration.Password));
        await IntegrationAssert.AssertErrorAsync(
            loginResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
        var loginBody = await TestApiClient.ReadBodyAsync(loginResponse);
        IntegrationAssert.AssertDoesNotContain(
            loginBody,
            registration.Password,
            "The dependency error must not contain the password.");

        var newRegistration = TestData.NewRegistration();
        using var registrationResponse = await _api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            newRegistration);
        await IntegrationAssert.AssertErrorAsync(
            registrationResponse,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
        var registrationBody = await TestApiClient.ReadBodyAsync(registrationResponse);
        IntegrationAssert.AssertDoesNotContain(
            registrationBody,
            newRegistration.Password,
            "The dependency error must not contain the password.");
    }
}
