using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Failure;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class PasswordChangePostCommitFailureTests
{
    private ApiIntegrationEnvironment _environment = null!;
    private TestApiClient _api = null!;
    private PostCommitRedisStopInterceptor _interceptor = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        PostCommitRedisStopInterceptor? interceptor = null;
        _environment = await ApiIntegrationEnvironment.StartAsync(
            new ApiIntegrationEnvironmentOptions
            {
                BindRedisPort = true,
                ConfigureTestServices = (services, environment) =>
                {
                    interceptor = new PostCommitRedisStopInterceptor(
                        () => environment.StopRedisAsync(save: true));
                    services.AddDbContext<AppDbContext>(options =>
                        options.AddInterceptors(interceptor));
                },
            });
        _interceptor = interceptor
            ?? throw new InvalidOperationException(
                "The post-commit interceptor was not registered.");
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
    public async Task PasswordChange_WithPostCommitRedisFailure_ShouldInvalidateAllSessions()
    {
        var registration = TestData.NewRegistration();
        var current = await TestFlows.RegisterAsync(_api, registration);
        var other = await TestFlows.LoginAsync(
            _api,
            new LoginWireRequest(registration.Email, registration.Password));
        var (_, etag) = await TestFlows.GetMeAsync(_api, current.AccessToken);

        _interceptor.Arm();

        var (readProfile, _) = await TestFlows.GetMeAsync(_api, current.AccessToken);
        Assert.That(
            readProfile.UserPublicId,
            Is.EqualTo(current.User.UserPublicId));
        Assert.That(
            _environment.TestRedis.IsConnected,
            Is.True,
            "Read transactions must not stop Redis.");

        using var response = await _api.SendJsonAsync(
            HttpMethod.Put,
            "/api/v2/me/password",
            new ChangePasswordWireRequest(
                registration.Password,
                TestData.NewPassword),
            current.AccessToken,
            etag);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);

        await _environment.WaitForAppRedisConnectionAsync(
            false,
            TimeSpan.FromSeconds(20));
        await _environment.StartRedisAsync();

        var currentBodyExists = await _environment.SessionBodyExistsAsync(
            current.AccessToken);
        var otherBodyExists = await _environment.SessionBodyExistsAsync(
            other.AccessToken);
        Assert.Multiple(() =>
        {
            Assert.That(
                currentBodyExists,
                Is.True,
                "The current session payload saved before the outage must survive.");
            Assert.That(
                otherBodyExists,
                Is.True,
                "The other session payload saved before the outage must survive.");
        });

        using (var currentResponse = await _api.GetAsync(
                   "/api/v2/me",
                   current.AccessToken))
        {
            await IntegrationAssert.AssertErrorAsync(
                currentResponse,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Auth.TokenInvalid);
        }

        using (var otherResponse = await _api.GetAsync(
                   "/api/v2/me",
                   other.AccessToken))
        {
            await IntegrationAssert.AssertErrorAsync(
                otherResponse,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Auth.TokenInvalid);
        }

        using (var oldPassword = await _api.SendJsonAsync(
                   HttpMethod.Post,
                   "/api/v2/auth/sessions",
                   new LoginWireRequest(
                       registration.Email,
                       registration.Password)))
        {
            await IntegrationAssert.AssertErrorAsync(
                oldPassword,
                HttpStatusCode.Unauthorized,
                ErrorCodes.Session.AuthInvalidCredentials);
        }

        var newLogin = await TestFlows.LoginAsync(
            _api,
            new LoginWireRequest(registration.Email, TestData.NewPassword));
        var (newProfile, _) = await TestFlows.GetMeAsync(_api, newLogin.AccessToken);
        Assert.That(
            newProfile.UserPublicId,
            Is.EqualTo(current.User.UserPublicId));

        var persisted = await _environment.FindUserAsync(
            current.User.UserPublicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                _environment.PasswordHasher.Verify(
                    persisted!.PasswordHash,
                    TestData.NewPassword),
                Is.True,
                "The new password must remain committed.");
            Assert.That(
                _environment.PasswordHasher.Verify(
                    persisted.PasswordHash,
                    registration.Password),
                Is.False,
                "The old password must not remain valid.");
            Assert.That(persisted.SecurityVersion, Is.GreaterThanOrEqualTo(2));
            Assert.That(persisted.Version, Is.GreaterThanOrEqualTo(2));
        });
    }
}
